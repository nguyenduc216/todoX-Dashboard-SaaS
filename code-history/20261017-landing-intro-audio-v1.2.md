# Landing Intro audio v1.2

- Keep initial autoplay browser-safe and muted; apply `MutedByDefault` only on user-initiated Play/Replay unless the viewer has explicitly chosen an audio state.
- Preserve selected audio across pause/resume and looping; synchronize sound icon and accessible label with actual muted state.
- Clarify Dashboard wording and autoplay restrictions without changing settings keys or storage.
- Run `node TodoX.Landing/scripts/landing-intro-video.test.js` for player scenarios.