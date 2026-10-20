const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, '../wwwroot/js/landing-intro-video.js'), 'utf8');

async function scenario(mode, mutedByDefault, options = {}) {
  const playButton = { hidden: false, setAttribute(key, value) { this[key] = value; }, click() { this.onclick(); } };
  const sound = { hidden: true, setAttribute(key, value) { this[key] = value; } };
  const video = {
    muted: false, paused: true, attempts: [],
    play() {
      this.attempts.push(this.muted);
      if (options.rejectFirst && this.attempts.length === 1) return Promise.reject(new Error('Autoplay blocked'));
      this.paused = false;
      return Promise.resolve();
    },
    pause() { this.paused = true; },
    removeAttribute() {}
  };
  const screen = {
    style: {}, classList: { add() {}, remove() {} },
    querySelector(selector) { return { '.intro-video': video, '.play': playButton, '.intro-sound': sound }[selector]; }
  };
  vm.runInNewContext(source, {
    document: { querySelector() { return screen; } },
    fetch: async () => ({ ok: true, json: async () => ({
      enabled: true, videoUrl: '/media/landing/intro/videos/test.mp4',
      playbackMode: mode, mutedByDefault, showControls: true
    }) }),
    window: { matchMedia: () => ({ matches: false }) }, navigator: {}
  });
  const settle = async () => { for (let i = 0; i < 8; i++) await Promise.resolve(); };
  await settle();
  return { video, playButton, sound, settle };
}

async function run() {
  for (const mode of ['AUTOPLAY_LOOP', 'AUTOPLAY_ONCE']) {
    const { video, playButton, sound, settle } = await scenario(mode, false);
    assert.deepEqual(video.attempts, [true]);
    assert.equal(sound.hidden, false);
    assert.equal(sound['aria-label'], 'Bật tiếng');
    sound.onclick();
    assert.equal(video.muted, false);
    assert.equal(sound.textContent, '🔊');
    assert.equal(sound['aria-label'], 'Tắt tiếng');
    video.pause(); playButton.onclick(); await settle();
    assert.equal(video.attempts.at(-1), false);
    if (mode === 'AUTOPLAY_ONCE') {
      video.onended();
      assert.equal(playButton['aria-label'], 'Phát lại video');
      playButton.onclick(); await settle();
      assert.equal(video.attempts.at(-1), false);
    } else {
      video.onended();
      assert.equal(video.muted, false);
    }
  }
  for (const muted of [false, true]) {
    const { video, playButton, sound, settle } = await scenario('CLICK_TO_PLAY', muted);
    assert.equal(video.attempts.length, 0);
    playButton.onclick(); await settle();
    assert.equal(video.attempts[0], muted);
    assert.equal(sound['aria-label'], muted ? 'Bật tiếng' : 'Tắt tiếng');
    sound.onclick();
    video.pause(); playButton.onclick(); await settle();
    assert.equal(video.attempts.at(-1), !muted);
  }
  for (const muted of [false, true]) {
    const { video, playButton, settle } = await scenario('AUTOPLAY_ONCE', muted);
    video.onended(); playButton.onclick(); await settle();
    assert.equal(video.attempts.at(-1), muted);
  }
  const blocked = await scenario('AUTOPLAY_ONCE', false, { rejectFirst: true });
  assert.equal(blocked.playButton.hidden, false);
  blocked.playButton.onclick(); await blocked.settle();
  assert.deepEqual(blocked.video.attempts, [true, false]);
  console.log('Landing Intro audio scenarios PASS');
}

run().catch(error => { console.error(error); process.exitCode = 1; });