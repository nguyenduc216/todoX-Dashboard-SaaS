# TodoX.EditVideoTool — kế hoạch xây dựng

> Trạng thái: hoàn tất bản 0.4.1 (03/11/2026) — xem REPORT-20261103.md · Nhánh: `feature/todox-agent-20261103` · Ngày: 2026-11-03

## 1. Mục tiêu

Một ứng dụng Windows (`todox-editvideo.exe`) cài trên máy khách, để Claude (Code/Desktop) và Codex
gọi qua **MCP (stdio)** để edit video **ngay trên máy khách**. Server todoX chỉ lo:
license, cấp bộ cài thành phần, giọng nói cá nhân, (tùy chọn) "kế hoạch edit" nhỏ dạng JSON.

Nguyên tắc:
- **Video không bao giờ upload lên todoX** (trừ mẫu giọng ngắn khi tạo voice cá nhân).
- **Bí quyết nằm trong code đã biên dịch**, không nằm trong prompt/skill. Skill chỉ là lớp vỏ mỏng.
- **License khóa theo máy** (device fingerprint + license file ký Ed25519 + heartbeat online).

## 2. Vị trí trong monorepo

```
todoX-Dashboard-SaaS/
├─ TodoX.Web/              (dashboard — thêm endpoint /api/agent/v1/*)
├─ TodoX.Landing/          (landing — thêm trang giới thiệu/tải về, giai đoạn cuối)
├─ TodoX.EditVideoTool/            ← MỚI: exe MCP server chạy trên máy khách
│  ├─ Mcp/                 tools MCP (video, voice, setup)
│  ├─ Licensing/           fingerprint, license file, heartbeat
│  ├─ Media/               FFmpeg runner, probe, scene detect, EDL executor
│  ├─ Remotion/            gọi render template (Node + Remotion bundle)
│  ├─ Setup/               cài thành phần, đăng ký MCP vào Claude/Codex
│  └─ Skill/               SKILL.md mỏng (copy vào ~/.claude/skills, ~/.codex)
├─ TodoX.EditVideoTool.Templates/  ← MỚI: project Remotion (TypeScript) — build thành bundle
├─ TodoX.EditVideoTool.Tests/      ← MỚI: xUnit
└─ docs/todox-agent/       tài liệu này + SQL thủ công
```

## 3. Kiến trúc

```
Claude Code / Claude Desktop / Codex
        │ stdio (MCP)
        ▼
todox-editvideo.exe ─────────── HTTPS (nhỏ, vài KB) ──────────► todoX.Web /api/agent/v1
  ├ License guard (offline verify + heartbeat 24h)            ├ activate / heartbeat / deactivate
  ├ ffmpeg.exe, ffprobe.exe  (%LOCALAPPDATA%\TodoX\EditVideoTool)      ├ components manifest (URL + SHA256)
  ├ node + Remotion bundle   (render template)                ├ voice profiles + TTS (tính point)
  └ Workspace: thư mục video của khách                        └ (P6) edit plan từ metadata
```

### Khóa license theo máy
1. `todox-editvideo activate <LICENSE-KEY>` → tính `device_hash = SHA256(MachineGuid + BIOS/board serial + system disk serial + salt)`.
2. Server kiểm tra key còn slot → lưu device → trả **license file** ký Ed25519: `{license_id, customer_id, device_hash, plan, features, expires_at, heartbeat_due}`.
3. Exe verify chữ ký bằng public key nhúng sẵn. Sai device_hash / quá hạn heartbeat (mặc định 7 ngày offline) → khóa tool.
4. Dashboard: khách xem thiết bị, bấm "Gỡ thiết bị" để chuyển máy. Admin thu hồi key.

### Xác thực API agent
Thêm `ICoreApiCallerAuthenticator` mới (`AgentDeviceAuthenticator`) vào chuỗi resolver sẵn có
(`TodoX.Web/Services/Platform/CoreApiCallerResolver.cs`). Request agent ký bằng token ngắn hạn
lấy từ heartbeat — không đụng luồng đăng nhập dashboard.

## 4. Các tool MCP (dự kiến)

| Tool | Mô tả | Chạy ở |
|---|---|---|
| `todox_status` | license, thành phần đã cài, số point | local + server |
| `todox_probe` | thông tin video (thời lượng, độ phân giải, audio) | local |
| `todox_auto_edit` | cắt cảnh, bỏ khoảng lặng, tỉ lệ 9:16/16:9, nhạc nền | local |
| `todox_cut` / `todox_concat` | cắt, ghép theo mốc thời gian | local |
| `todox_subtitle` | tạo phụ đề (whisper local, giai đoạn sau) + burn | local |
| `todox_render_template` | intro/outro/text động bằng Remotion | local |
| `todox_voice_list` / `todox_voice_tts` | đọc lời bằng giọng cá nhân → file wav/mp3 về máy | server (trả audio nhỏ) |
| `todox_voice_mix` | ghép giọng vào video, ducking nhạc nền | local |

Mô tả tool ngắn, không lộ tham số nội bộ; logic preset nằm trong code.

## 5. Endpoint mới trên todoX.Web (`/api/agent/v1`)

| Method | Path | Mục đích |
|---|---|---|
| POST | `/activate` | kích hoạt key trên thiết bị, trả license file ký |
| POST | `/heartbeat` | gia hạn, trả token ngắn hạn + trạng thái thu hồi |
| POST | `/deactivate` | gỡ thiết bị từ chính máy |
| GET | `/components` | manifest thành phần (ffmpeg, node, remotion bundle): version, URL, SHA256 |
| GET | `/me` | gói, tính năng, số dư point |
| GET/POST | `/voices` | danh sách / tạo voice cá nhân (upload mẫu giọng) |
| POST | `/voices/{id}/tts` | tổng hợp giọng → trả URL audio (tính point qua ví sẵn có) |
| GET | `/releases/latest` | auto-update exe |

Trang dashboard mới: **Admin › License Agent** (cấp/thu hồi key), **Khách › Thiết bị TodoX Agent**, **Tải TodoX Agent**.

## 6. Database (theo AGENTS.md: chỉ script SQL thủ công, không migration)

File dự kiến: `docs/todox-agent/sql/V0xx_Agent_Licensing.sql` — **bạn chạy tay**, mình không chạy vào DB.
Bảng mới (không sửa bảng cũ):
- `agent_licenses` (id, customer_id, license_key_hash, plan_code, max_devices, status, expires_at, …)
- `agent_devices` (id, license_id, device_hash, device_name, os, agent_version, activated_at, last_seen_at, revoked_at)
- `agent_events` (audit: activate/heartbeat/deactivate/revoke, ip)
- `agent_voice_profiles` (id, customer_id, provider, provider_voice_id, name, status, …)
- `agent_releases` (version, channel, url, sha256, min_supported, published_at)

## 7. Cài đặt (installer)

- Bộ cài **Inno Setup** (`TodoX-EditVideoTool-Setup.exe`), cài vào `%LOCALAPPDATA%\TodoX\EditVideoTool` (không cần quyền admin).
- Bước cài: kích hoạt key → tải thành phần theo manifest (verify SHA256) → đăng ký MCP:
  - Claude Code: `claude mcp add todox -- "<path>\todox-editvideo.exe" mcp`
  - Claude Desktop: thêm vào `%APPDATA%\Claude\claude_desktop_config.json`
  - Codex: thêm `[mcp_servers.todox]` vào `~/.codex/config.toml`
  - Copy skill mỏng vào `~/.claude/skills/todox-video/` và thư mục skill của Codex.
- Lệnh `todox-editvideo doctor` kiểm tra mọi thứ, `todox-editvideo uninstall` gỡ sạch đăng ký.

## 8. Lộ trình (mỗi giai đoạn = 1 nhánh/PR, build+test+commit+push theo AGENTS.md)

| # | Giai đoạn | Kết quả | Cần bạn |
|---|---|---|---|
| P0 | Skeleton | `TodoX.EditVideoTool` + Tests trong solution, MCP stdio chạy `todox_status`, test với Claude Code trên máy này | duyệt tên/plan |
| P1 | License server | SQL script + endpoint activate/heartbeat/deactivate + authenticator + test | **chạy SQL** trên DB dev |
| P2 | License client | fingerprint, license file, guard, lệnh activate/doctor | thử kích hoạt |
| P3 | Remotion engine | project template (TypeScript), bundle, `todox_render_template`, `todox_auto_edit` dựng timeline Remotion | chọn mẫu intro/outro, gửi 1–2 video mẫu |
| P4 | Tiện ích FFmpeg | probe/cut/concat/chuẩn hóa input, trích audio, preset 9:16 (phục vụ Remotion) | — |
| P5 | Voice cá nhân (Vbee qua todoX) | endpoint voice + TTS dùng tài khoản Vbee của todoX, tính point, tool voice + mix | duyệt giá point |
| P6 | Installer + update | Inno Setup, components manifest, auto-update, ký code | mua cert ký code (tùy chọn) |
| P7 | Dashboard/Landing UI | trang license, thiết bị, tải về, trang giới thiệu | duyệt UI |

## 9. Rủi ro / việc cần quyết

- **Remotion (ưu tiên dùng):** engine dựng chính vì video sinh động hơn hẳn FFmpeg (animation React, text động, chuyển cảnh, layout).
  License: miễn phí cho cá nhân/công ty ≤3 người; lớn hơn cần *Company License* (remotion.pro). Không có khóa kỹ thuật —
  là nghĩa vụ pháp lý. Trước khi bán: gửi email Remotion xác nhận gói cho "app desktop phân phối, khách tự render".
- **Voice:** Vbee, toàn bộ gọi qua endpoint todoX bằng tài khoản Vbee của todoX (khách không có key Vbee).
- **FFmpeg license:** bản "full" (GPL, có x264) phân phối kèm sản phẩm đóng → vướng GPL.
  Đề xuất: tải **bản LGPL** từ nguồn chính thức lúc cài (không đóng gói), encode bằng phần cứng (NVENC/QSV/AMF) hoặc OpenH264.
- **Voice cá nhân:** luồng Vbee hiện tại (contract VOICE/AUDIO) đóng băng → làm service agent mới, tái dùng `IVbeeVoiceClient`
  mà không sửa nó. Clone giọng cần kiểm tra API voice clone của Vbee + **cam kết đồng ý của chủ giọng**.
- **SmartScreen/antivirus:** exe chưa ký sẽ bị cảnh báo; nên có chứng chỉ ký code (OV/EV).
- **Crack:** không chống tuyệt đối; giảm rủi ro bằng heartbeat, token ngắn hạn, tính năng quý gọi server.

## 10. Cách làm việc qua điện thoại

- Mỗi khi cần bạn quyết/duyệt (chạy SQL, chọn provider, push, chi tiền), mình dừng và hỏi bằng câu hỏi có nút bấm.
- Kết thúc mỗi giai đoạn: báo nhánh, commit SHA, kết quả build/test.
- Không tự chạy SQL vào DB, không tạo/chi tiền, không push lên `main` khi chưa được duyệt.
