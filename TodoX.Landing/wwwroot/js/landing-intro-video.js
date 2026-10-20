(() => {
  const screen = document.querySelector('.hero-visual .phone-screen');
  if (!screen) return;
  const video = screen.querySelector('.intro-video');
  const play = screen.querySelector('.play');
  const sound = screen.querySelector('.intro-sound');
  let state = 'poster';
  const setState = next => {
    state = next;
    play.hidden = next === 'playing';
    play.setAttribute('aria-label', next === 'ended' ? 'Phát lại video' : next === 'paused' ? 'Tiếp tục video' : 'Phát video');
  };
  fetch('/api/landing/intro-video', { credentials: 'same-origin' })
    .then(response => { if (!response.ok) throw new Error('Settings unavailable'); return response.json(); })
    .then(config => {
      const mediaUrl = url => typeof url === 'string' && /^\/media\/landing\/intro\/(videos|posters)\/[a-zA-Z0-9_.%-]+$/.test(url);
      if (!config.enabled || !mediaUrl(config.videoUrl) || !['AUTOPLAY_LOOP', 'CLICK_TO_PLAY', 'AUTOPLAY_ONCE'].includes(config.playbackMode)) return;
      if (mediaUrl(config.posterUrl)) screen.style.backgroundImage = `linear-gradient(to top,rgba(0,0,0,.85),rgba(0,0,0,.08) 50%),url("${config.posterUrl}")`;
      screen.classList.add('intro-ready');
      video.poster = mediaUrl(config.posterUrl) ? config.posterUrl : '/img/landing/chatstaff-hero-phone.png';
      video.loop = config.playbackMode === 'AUTOPLAY_LOOP';
      video.muted = true;
      let selectedMuted = null;
      const syncSound = () => {
        sound.textContent = video.muted ? '🔇' : '🔊';
        sound.setAttribute('aria-label', video.muted ? 'Bật tiếng' : 'Tắt tiếng');
      };
      const start = (manual, resume = false) => {
        if (manual && !resume) video.muted = selectedMuted ?? !!config.mutedByDefault;
        syncSound();
        video.play().then(() => setState('playing')).catch(() => setState('poster'));
      };
      syncSound();
      video.preload = config.playbackMode === 'CLICK_TO_PLAY' ? 'none' : 'metadata';
      video.src = config.videoUrl;
      sound.hidden = !config.showControls;
      sound.onclick = () => { video.muted = !video.muted; selectedMuted = video.muted; syncSound(); };
      play.onclick = () => {
        start(true, state === 'paused');
      };
      video.onclick = () => { if (video.paused) play.click(); else { video.pause(); setState('paused'); } };
      video.onended = () => setState('ended');
      video.onvolumechange = syncSound;
      video.onerror = () => { video.removeAttribute('src'); screen.classList.remove('intro-ready'); sound.hidden = true; setState('poster'); };
      if (config.playbackMode !== 'CLICK_TO_PLAY' && !window.matchMedia('(prefers-reduced-motion: reduce)').matches && !navigator.connection?.saveData) {
        start(false);
      }
    }).catch(() => {});
})();