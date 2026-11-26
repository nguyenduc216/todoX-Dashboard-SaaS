using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Mcp;
using TodoX.EditVideoTool.Projects;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.App;

/// <summary>
/// "Tạo video từ sản phẩm": the customer adds a product photo and a few lines about it, TodoX's 79AI script agent
/// (the one the dashboard uses) writes the scenes, the customer can edit them, then TodoX makes an AI image and an AI
/// video per scene and edits them with narration and captions — no Claude needed. Paid steps go through the usual
/// TodoX price window when the customer asked to be asked.
/// </summary>
internal sealed class ProductVideoForm : Form
{
    private static readonly string[] Aspects = ["9:16", "16:9", "1:1"];
    private static readonly int[] Lengths = [15, 20, 30, 45, 60];
    private static readonly string[] PhotoTypes = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly IServiceProvider _services;
    private readonly TodoXServerClient _server;
    private readonly string _project;
    private readonly CancellationTokenSource _closing = new();

    private readonly TextBox _photo = Box("Ảnh sản phẩm (.jpg, .png, .webp)");
    private readonly PictureBox _preview = new() { Size = new Size(120, 120), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.Panel, Margin = new Padding(0, 4, 0, 8) };
    private readonly TextBox _name = Box("Tên sản phẩm, ví dụ: Serum dưỡng ẩm X 30ml");
    private readonly TextBox _price = Box("Giá / ưu đãi, ví dụ: 299k, mua 2 tặng 1");
    private readonly TextBox _highlights = Box("Điểm nổi bật: mỗi dòng một ý", lines: 4);
    private readonly TextBox _contact = Box("Liên hệ / kêu gọi, ví dụ: Inbox ngay hoặc gọi 09xx");
    private readonly TextBox _extra = Box("Yêu cầu thêm (không bắt buộc): phong cách, người xem…", lines: 2);
    private readonly ComboBox _service = Pick();
    private readonly ComboBox _length = Pick();
    private readonly ComboBox _aspect = Pick();
    private readonly ComboBox _imageModel = Pick();
    private readonly ComboBox _imageResolution = Pick();
    private readonly ComboBox _videoModel = Pick();
    private readonly ComboBox _videoResolution = Pick();
    private readonly ComboBox _videoMode = Pick();
    private readonly ComboBox _voice = Pick();
    private readonly DataGridView _scenes = new();
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Ui.Card, ForeColor = Ui.Muted, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f) };
    private readonly Button _write = Ui.Button("1. Viết kịch bản", primary: true);
    private readonly Button _make = Ui.Button("2. Tạo video");
    private readonly Button _open = Ui.Button("Mở video");
    private string? _output;
    private bool _busy;

    private sealed record Item(string Text, string? Value)
    {
        public override string ToString() => Text;
    }

    public ProductVideoForm(IServiceProvider services, string project)
    {
        _services = services;
        _server = services.GetRequiredService<TodoXServerClient>();
        _project = project;

        Text = $"Tạo video từ sản phẩm — {project}";
        Icon = WindowsNotifier.AppIcon();
        BackColor = Ui.Ground;
        ForeColor = Ui.Ink;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1000, 680);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new Size(Math.Min(1240, area.Width - 60), Math.Min(820, area.Height - 60));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(24, 18, 24, 18), BackColor = Ui.Ground };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(new ScrollHost(LeftSide(), Ui.Ground) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 18, 0) }, 0, 0);
        root.Controls.Add(RightSide(), 1, 0);
        Controls.Add(root);

        foreach (var a in Aspects)
        {
            _aspect.Items.Add(new Item(a, a));
        }

        foreach (var s in Lengths)
        {
            _length.Items.Add(new Item($"{s} giây", s.ToString()));
        }

        _aspect.SelectedIndex = 0;
        _length.SelectedIndex = 2;
        _imageModel.SelectedIndexChanged += (_, _) => Fill(_imageResolution, (_imageModel.SelectedItem as AiModelChoice)?.Resolutions);
        _videoModel.SelectedIndexChanged += (_, _) =>
        {
            var m = _videoModel.SelectedItem as AiModelChoice;
            Fill(_videoResolution, m?.Resolutions);
            Fill(_videoMode, m?.Modes);
        };
        _photo.TextChanged += (_, _) => ShowPhoto();
        _write.Click += async (_, _) => await WriteAsync();
        _make.Click += async (_, _) => await MakeAsync();
        _open.Click += (_, _) =>
        {
            if (_output is not null)
            {
                Ui.Open(_output);
            }
        };
        _make.Enabled = false;
        _open.Visible = false;

        Shown += async (_, _) => await LoadChoicesAsync();
        FormClosing += (_, e) =>
        {
            if (_busy && MessageBox.Show(this, "TodoX đang tạo video. Đóng cửa sổ sẽ dừng ở bước hiện tại (các phần AI đã tạo vẫn nằm trong thư mục 06-ai). Đóng?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _closing.Cancel();
        };
    }

    // ------------------------------------------------------------------ layout

    private Control LeftSide()
    {
        var left = Ui.Stack(400);
        left.Controls.Add(Ui.Section("Sản phẩm", top: 0));
        left.Controls.Add(new Label { Text = "Ảnh sản phẩm", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, 2) });
        var photoRow = Ui.Row();
        _photo.Width = 262;
        var browse = Ui.Button("Chọn ảnh…");
        browse.Margin = new Padding(0, 3, 0, 0);
        browse.Click += (_, _) => BrowsePhoto();
        photoRow.Controls.AddRange([_photo, browse]);
        left.Controls.Add(photoRow);
        left.Controls.Add(_preview);
        Labelled(left, "Tên sản phẩm *", _name);
        Labelled(left, "Giá / ưu đãi", _price);
        Labelled(left, "Điểm nổi bật (mỗi dòng một ý)", _highlights);
        Labelled(left, "Liên hệ / kêu gọi mua", _contact);
        Labelled(left, "Yêu cầu thêm (không bắt buộc)", _extra);

        left.Controls.Add(Ui.Section("Video"));
        Labelled(left, "Kiểu video (agent viết kịch bản)", _service);
        Labelled(left, "Độ dài", _length);
        Labelled(left, "Khung hình", _aspect);
        Labelled(left, "Giọng đọc", _voice);

        left.Controls.Add(Ui.Section("Model AI"));
        Labelled(left, "Model ảnh", _imageModel);
        Labelled(left, "Độ phân giải ảnh", _imageResolution);
        Labelled(left, "Model video", _videoModel);
        Labelled(left, "Độ phân giải video", _videoResolution);
        Labelled(left, "Chế độ video", _videoMode);
        left.Controls.Add(Ui.Text("Ảnh, video AI và giọng đọc trừ điểm TodoX theo từng lần tạo (giá xem trên Dashboard). Viết kịch bản không trừ điểm.", 9, Ui.Muted, width: 390));
        return left;
    }

    private Control RightSide()
    {
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Ui.Ground, Margin = new Padding(0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        right.Controls.Add(Ui.Section("Kịch bản (sửa trực tiếp trong bảng nếu muốn)", top: 0), 0, 0);
        _scenes.Dock = DockStyle.Fill;
        _scenes.BackgroundColor = Ui.Card;
        _scenes.BorderStyle = BorderStyle.None;
        _scenes.GridColor = Ui.Line;
        _scenes.RowHeadersVisible = false;
        _scenes.AllowUserToAddRows = false;
        _scenes.AllowUserToResizeRows = false;
        // Long prompts: fixed-height rows read from the top; the full text shows while editing a cell.
        _scenes.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _scenes.RowTemplate.Height = 110;
        _scenes.EnableHeadersVisualStyles = false;
        _scenes.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Ui.Panel, ForeColor = Ui.Muted, SelectionBackColor = Ui.Panel, Font = new Font("Segoe UI", 9) };
        _scenes.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Ui.Card, ForeColor = Ui.Ink, SelectionBackColor = Ui.CardHover, SelectionForeColor = Ui.Ink, WrapMode = DataGridViewTriState.True, Alignment = DataGridViewContentAlignment.TopLeft };
        _scenes.Columns.Add(new DataGridViewTextBoxColumn { Name = "sec", HeaderText = "Giây", Width = 52 });
        _scenes.Columns.Add(new DataGridViewTextBoxColumn { Name = "voice", HeaderText = "Lời đọc", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 34 });
        _scenes.Columns.Add(new DataGridViewTextBoxColumn { Name = "image", HeaderText = "Ảnh cảnh (mô tả cho AI)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 38 });
        _scenes.Columns.Add(new DataGridViewTextBoxColumn { Name = "motion", HeaderText = "Chuyển động", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 28 });
        Ui.DarkScrollBars(_scenes);
        right.Controls.Add(_scenes, 0, 1);

        right.Controls.Add(Ui.Section("Tiến trình"), 0, 2);
        right.Controls.Add(_log, 0, 3);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 10, 0, 0), BackColor = Ui.Ground };
        foreach (var b in new[] { _write, _make, _open })
        {
            b.Height = 40;
            b.Margin = new Padding(0, 0, 10, 0);
        }

        buttons.Controls.AddRange([_write, _make, _open]);
        right.Controls.Add(buttons, 0, 4);
        return right;
    }

    private static void Labelled(Control parent, string label, Control control)
    {
        parent.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(0, 6, 0, 2), UseMnemonic = false });
        parent.Controls.Add(control);
    }

    private static TextBox Box(string placeholder, int lines = 1) => new()
    {
        Width = 390,
        Multiline = lines > 1,
        Height = lines > 1 ? 22 * lines : 28,
        ScrollBars = lines > 1 ? ScrollBars.Vertical : ScrollBars.None,
        Font = new Font("Segoe UI", 10.5f),
        BackColor = Ui.Panel,
        ForeColor = Ui.Ink,
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = placeholder,
        Margin = new Padding(0, 4, 8, 6)
    };

    private static ComboBox Pick() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 390, BackColor = Ui.Panel, ForeColor = Ui.Ink, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 4)
    };

    private static void Fill(ComboBox box, IReadOnlyList<string>? values)
    {
        box.Items.Clear();
        foreach (var v in values ?? [])
        {
            box.Items.Add(new Item(v, v));
        }

        box.Enabled = box.Items.Count > 0;
        if (box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
        }
    }

    // ------------------------------------------------------------------ data

    private async Task LoadChoicesAsync()
    {
        Say("Đang tải kiểu video, model AI và giọng đọc từ TodoX…");
        _write.Enabled = false;
        var ct = _closing.Token;
        var services = await _server.AgentServicesAsync(ct);
        var models = await _server.AiModelsAsync(null, null, ct);
        var voices = await _server.VoicesAsync(ct);
        if (IsDisposed)
        {
            return;
        }

        if (!services.Ok || !models.Ok)
        {
            Say(Problem(services.Ok ? models.ErrorCode : services.ErrorCode, services.Ok ? models.Message : services.Message));
            return;
        }

        if (services.Value.TryGetProperty("services", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in list.EnumerateArray())
            {
                _service.Items.Add(new Item(s.GetProperty("name").GetString() ?? "", s.GetProperty("id").GetString()));
            }
        }

        foreach (var m in ProductVideoMaker.Models(models.Value, "image"))
        {
            _imageModel.Items.Add(m);
        }

        // Video models that start from an image (each scene's AI image); all of them when none says so.
        var videoModels = ProductVideoMaker.Models(models.Value, "video");
        foreach (var m in videoModels.Any(m => m.Abilities.Contains("startImage")) ? videoModels.Where(m => m.Abilities.Contains("startImage")) : videoModels)
        {
            _videoModel.Items.Add(m);
        }

        if (voices.Ok && voices.Value.TryGetProperty("voices", out var vs) && vs.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in vs.EnumerateArray())
            {
                var label = v.GetProperty("name").GetString() + (v.TryGetProperty("gender", out var g) && g.ValueKind == JsonValueKind.String ? $" ({g.GetString()})" : "");
                _voice.Items.Add(new Item(label, v.GetProperty("code").GetString()));
                if (v.TryGetProperty("isDefault", out var d) && d.ValueKind == JsonValueKind.True)
                {
                    _voice.SelectedIndex = _voice.Items.Count - 1;
                }
            }
        }

        foreach (var box in new[] { _service, _imageModel, _videoModel, _voice })
        {
            if (box.SelectedIndex < 0 && box.Items.Count > 0)
            {
                box.SelectedIndex = 0;
            }
        }

        if (_service.Items.Count == 0)
        {
            Say("Chưa có kiểu video nào bật agent viết kịch bản. Quản trị TodoX cần bật Prompt Assistant cho một dịch vụ trên Dashboard.");
            return;
        }

        if (_imageModel.Items.Count == 0 || _videoModel.Items.Count == 0)
        {
            Say("TodoX chưa mở model ảnh / video AI nào cho tài khoản này.");
            return;
        }

        PickProjectPhoto();
        _write.Enabled = true;
        Say("Sẵn sàng. Thêm ảnh và thông tin sản phẩm rồi bấm \"1. Viết kịch bản\".");
    }

    private void PickProjectPhoto()
    {
        var folder = _services.GetRequiredService<ProjectFolders>().Find(_project, create: true).Folder?.Path;
        var images = folder is null ? null : Path.Combine(folder, ProjectFolders.ImagesDir);
        if (_photo.Text.Length == 0 && images is not null && Directory.Exists(images))
        {
            _photo.Text = Directory.EnumerateFiles(images).FirstOrDefault(f => PhotoTypes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)) ?? "";
        }
    }

    private void BrowsePhoto()
    {
        using var dialog = new OpenFileDialog { Filter = "Ảnh sản phẩm|*.jpg;*.jpeg;*.png;*.webp", Title = "Chọn ảnh sản phẩm" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _photo.Text = dialog.FileName;
        }
    }

    private void ShowPhoto()
    {
        _preview.Image?.Dispose();
        _preview.Image = null;
        try
        {
            if (File.Exists(_photo.Text))
            {
                using var stream = File.OpenRead(_photo.Text);
                using var image = Image.FromStream(stream);
                _preview.Image = new Bitmap(image);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            // Not a readable picture; the check before making the video says so.
        }
    }

    // ------------------------------------------------------------------ steps

    private async Task WriteAsync()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            Say("Nhập tên sản phẩm.");
            _name.Focus();
            return;
        }

        if ((_service.SelectedItem as Item)?.Value is not { } serviceId || !Guid.TryParse(serviceId, out var service))
        {
            Say("Chọn kiểu video.");
            return;
        }

        var seconds = int.Parse(((Item)_length.SelectedItem!).Value!);
        var brief = ProductVideoMaker.Brief(_name.Text, _price.Text, _highlights.Text, _contact.Text, _extra.Text);
        await RunAsync("Agent TodoX đang viết kịch bản (thường 30 giây – 2 phút)…", async ct =>
        {
            var script = await _server.AgentScriptAsync(service, brief, seconds, ((Item)_aspect.SelectedItem!).Value, ct);
            if (!script.Ok)
            {
                // The reason code (no secrets) helps TodoX support find the agent's error.
                var reason = script.Detail is { } d && d.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                    ? $" (mã: {r.GetString()}{(d.TryGetProperty("provider", out var pm) && pm.ValueKind == JsonValueKind.String ? " — " + pm.GetString() : "")})"
                    : "";
                Say(Problem(script.ErrorCode, script.Message) + reason);
                return;
            }

            var scenes = ProductVideoMaker.Scenes(script.Value);
            _scenes.Rows.Clear();
            foreach (var s in scenes)
            {
                _scenes.Rows.Add(s.Seconds, s.Voice, s.ImagePrompt, s.MotionPrompt);
            }

            if (string.IsNullOrWhiteSpace(_contact.Text) && script.Value.TryGetProperty("cta", out var cta) && cta.ValueKind == JsonValueKind.String)
            {
                _contact.Text = cta.GetString();
            }

            _make.Enabled = scenes.Count > 0;
            Ui.Style(_make, primary: true);
            Say($"Agent đã viết {scenes.Count} cảnh, tổng {scenes.Sum(s => s.Seconds)} giây. Sửa nếu muốn rồi bấm \"2. Tạo video\".");
        });
    }

    private async Task MakeAsync()
    {
        if (!File.Exists(_photo.Text) || !PhotoTypes.Contains(Path.GetExtension(_photo.Text), StringComparer.OrdinalIgnoreCase))
        {
            Say("Chọn ảnh sản phẩm (.jpg, .png hoặc .webp).");
            return;
        }

        var scenes = ReadScenes();
        if (scenes.Count == 0)
        {
            Say("Kịch bản chưa có cảnh nào đủ lời đọc và mô tả ảnh.");
            return;
        }

        var options = new ProductVideoOptions(_project, _photo.Text, ((Item)_aspect.SelectedItem!).Value!,
            (AiModelChoice)_imageModel.SelectedItem!, (_imageResolution.SelectedItem as Item)?.Value,
            (AiModelChoice)_videoModel.SelectedItem!, (_videoResolution.SelectedItem as Item)?.Value, (_videoMode.SelectedItem as Item)?.Value,
            (_voice.SelectedItem as Item)?.Value, _contact.Text);
        var tools = ActivatorUtilities.CreateInstance<VideoTools>(_services);
        var maker = new ProductVideoMaker(tools, _services.GetRequiredService<ProjectFolders>());
        await RunAsync($"Bắt đầu tạo video {scenes.Count} cảnh. Có thể mất 5–20 phút tuỳ model; cứ để cửa sổ mở.", async ct =>
        {
            _output = await maker.MakeAsync(scenes, options, message => BeginInvoke(() => Say(message)), ct);
            _open.Visible = true;
            Ui.Style(_open, primary: true);
            Say($"Xong! Video: {_output}. Bấm \"Mở video\" để xem; bấm Đồng bộ ở màn hình chính để đưa lên Dashboard.");
        });
    }

    /// <summary>Window checks only (TODOX_WINDOW_SHOTS): fills a sample product and asks the agent for a script; nothing is paid.</summary>
    internal async Task SampleScriptAsync()
    {
        _name.Text = "Serum dưỡng ẩm Hydra B5 30ml";
        _price.Text = "299.000đ, mua 2 tặng 1 trong tuần này";
        _highlights.Text = string.Join(Environment.NewLine, "Thấm nhanh, không nhờn", "Cấp ẩm 24 giờ", "Dùng được cho da nhạy cảm");
        _contact.Text = "Inbox fanpage hoặc gọi 0900 000 000";
        for (var i = 0; i < 120 && !_write.Enabled; i++)
        {
            await Task.Delay(500);
        }

        Say("Model ảnh: " + _imageModel.SelectedItem + " | model video: " + _videoModel.SelectedItem + " | giọng: " + _voice.SelectedItem);
        for (var i = 0; i < _service.Items.Count && _scenes.Rows.Count == 0; i++)
        {
            _service.SelectedIndex = i;
            Say("Kiểu video: " + _service.SelectedItem);
            await WriteAsync();
        }

        if (Environment.GetEnvironmentVariable("TODOX_WINDOW_SHOTS") is { Length: > 0 } dir)
        {
            File.WriteAllText(Path.Combine(dir, "agent-log.txt"), _log.Text + Environment.NewLine + string.Join(Environment.NewLine + "---" + Environment.NewLine,
                ReadScenes().Select(s => $"{s.Seconds}s | {s.Voice}{Environment.NewLine}IMG: {s.ImagePrompt}{Environment.NewLine}MOTION: {s.MotionPrompt}")));
        }
    }

    private List<ProductScene> ReadScenes()
    {
        var list = new List<ProductScene>();
        foreach (DataGridViewRow row in _scenes.Rows)
        {
            var scene = new ProductScene
            {
                Seconds = int.TryParse(row.Cells["sec"].Value?.ToString(), out var s) ? Math.Clamp(s, 2, 15) : 5,
                Voice = row.Cells["voice"].Value?.ToString()?.Trim() ?? "",
                ImagePrompt = row.Cells["image"].Value?.ToString()?.Trim() ?? "",
                MotionPrompt = row.Cells["motion"].Value?.ToString()?.Trim() ?? ""
            };
            if (scene.Voice.Length > 0 && scene.ImagePrompt.Length > 0)
            {
                list.Add(scene);
            }
        }

        return list;
    }

    private async Task RunAsync(string start, Func<CancellationToken, Task> work)
    {
        _busy = true;
        _write.Enabled = _make.Enabled = false;
        UseWaitCursor = true;
        Say(start);
        try
        {
            await work(_closing.Token);
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException or ArgumentException or HttpRequestException)
        {
            Say("Dừng lại: " + ex.Message);
        }
        finally
        {
            _busy = false;
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                _write.Enabled = true;
                _make.Enabled = _scenes.Rows.Count > 0;
            }
        }
    }

    private void Say(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private static string Problem(string? code, string? message) => code switch
    {
        LicenseApiClient.NetworkError => ServerConnectivity.OfflineText,
        "invalid_request" or "not_found" or "ai_failed" or "ai_service_unavailable" => message ?? code,
        _ => LicenseMessages.ForServerError(code, message)
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview.Image?.Dispose();
            _closing.Dispose();
        }

        base.Dispose(disposing);
    }
}
