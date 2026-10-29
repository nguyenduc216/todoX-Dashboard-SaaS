# TodoX /bat-dong-san — UI handoff for Codex

Open `index.html` in a browser. It is a responsive **visual layout reference**, NOT a production implementation.

Reference images:
- `assets/desktop-reference.png` — original approved desktop concept.
- `assets/mobile-reference.png` — video-first approved mobile concept.

Use the HTML structure for layout, relative dimensions, color palette and section order. Some images in `assets/` are CROPS OF CONCEPT RENDERS solely for visual placeholders; they are not official product photos or source video assets. The phone poster is an illustration, **not a playable video**. HTML form intentionally does not submit anything.

Required production integration:
1. Exact public route `/bat-dong-san`, direct HTTP GET and refresh support.
2. Public Video Intro configuration from existing `GET /api/landing/intro-video` and media URL; preserve 9:16 pixel aspect ratio in the real production player. On mobile the poster/player goes FIRST and is as wide as practical. Do not crop/warp actual 9:16 video even though the CSS mockup phone footprint is shorter for layout illustration.
3. Lead form from existing `POST /api/contact-leads` with consent, validation, honeypot, rate limiting, source/UTM tracking. `industry='Bất động sản'`. Do NOT use mockup `onsubmit` in production.
4. Reuse current site header/footer styles where feasible. Maintain existing homepage functionality.
5. Five services, exactly as shown. No fabricated performance metrics.
6. Responsive target 320/375/390/430px mobile, 768/1024px tablet, 1440/1920px desktop.
7. Do not embed images from this mockup into production without asset approval; use approved available images or separate media plan.

Repository placement proposal (untracked design handoff):
`design-reference/bat-dong-san-v1/index.html`, and `design-reference/bat-dong-san-v1/assets/*`.

For screenshots, use a locally running static server or open this HTML file directly. Since no browser test was run here, verify visual fidelity yourself before marking PASS.
