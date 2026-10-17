(() => {
  const menu = document.querySelector('.menu-toggle');
  const nav = document.getElementById('navigation');
  menu.addEventListener('click', () => {
    const open = menu.getAttribute('aria-expanded') !== 'true';
    menu.setAttribute('aria-expanded', String(open));
    nav.classList.toggle('open', open);
  });
  nav.addEventListener('click', event => {
    if (event.target.closest('a')) { nav.classList.remove('open'); menu.setAttribute('aria-expanded', 'false'); }
  });

  const video = document.getElementById('demoVideo');
  const frame = document.getElementById('demoFrame');
  const play = document.getElementById('demoPlay');
  const sound = document.getElementById('demoSound');
  const fullscreen = document.getElementById('demoFullscreen');
  const hint = document.getElementById('demoHint');
  const validMedia = url => typeof url === 'string' && /^\/media\/landing\/intro\/(videos|posters)\/[a-zA-Z0-9_.%-]+$/.test(url);
  let selectedMuted = null;
  let settings;
  let state = 'poster';
  const setState = next => {
    state = next;
    frame.classList.toggle('is-playing', next === 'playing');
    play.hidden = next === 'playing';
    const label = next === 'ended' ? 'Phát lại video' : next === 'paused' ? 'Tiếp tục video' : 'Phát video';
    play.setAttribute('aria-label', label);
    play.querySelector('span').textContent = label;
  };
  const syncSound = () => {
    sound.textContent = video.muted ? '🔇' : '🔊';
    sound.setAttribute('aria-label', video.muted ? 'Bật tiếng' : 'Tắt tiếng');
  };
  const start = (manual, resume = false) => {
    if (manual && !resume) video.muted = selectedMuted ?? !!settings.mutedByDefault;
    syncSound();
    video.play().then(() => setState('playing')).catch(() => { hint.textContent = 'Video hiện chưa sẵn sàng. Vui lòng thử lại sau.'; setState('poster'); });
  };
  fetch('/api/landing/intro-video', { credentials: 'same-origin' })
    .then(response => { if (!response.ok) throw new Error('Video unavailable'); return response.json(); })
    .then(config => {
      if (!config.enabled || !validMedia(config.videoUrl) || !['AUTOPLAY_LOOP', 'AUTOPLAY_ONCE', 'CLICK_TO_PLAY'].includes(config.playbackMode)) return;
      settings = config;
      if (validMedia(config.posterUrl)) {
        const poster = new Image();
        poster.onload = () => { frame.style.setProperty('--intro-poster', `url("${config.posterUrl}")`); frame.classList.add('has-poster'); };
        poster.onerror = () => { frame.classList.remove('has-poster'); frame.style.removeProperty('--intro-poster'); };
        poster.src = config.posterUrl;
      }
      video.loop = config.playbackMode === 'AUTOPLAY_LOOP';
      video.muted = true;
      video.preload = config.playbackMode === 'CLICK_TO_PLAY' ? 'none' : 'metadata';
      video.src = config.videoUrl;
      sound.hidden = !config.showControls;
      fullscreen.hidden = !document.fullscreenEnabled;
      syncSound();
      play.addEventListener('click', () => start(true, state === 'paused'));
      video.addEventListener('click', () => { if (video.paused) start(true, state === 'paused'); else { video.pause(); setState('paused'); } });
      video.addEventListener('pause', () => { if (!video.ended) setState('paused'); });
      sound.addEventListener('click', () => { video.muted = !video.muted; selectedMuted = video.muted; syncSound(); });
      fullscreen.addEventListener('click', () => { if (document.fullscreenElement) document.exitFullscreen(); else if (frame.requestFullscreen) frame.requestFullscreen(); });
      video.addEventListener('volumechange', syncSound);
      video.addEventListener('ended', () => setState('ended'));
      video.addEventListener('error', () => { video.removeAttribute('src'); frame.classList.remove('has-poster'); sound.hidden = true; fullscreen.hidden = true; hint.textContent = 'Video hiện chưa sẵn sàng. Vui lòng thử lại sau.'; setState('poster'); });
      if (config.playbackMode !== 'CLICK_TO_PLAY' && !window.matchMedia('(prefers-reduced-motion: reduce)').matches && !navigator.connection?.saveData) start(false);
    }).catch(() => { hint.textContent = 'Video hiện chưa sẵn sàng. Vui lòng thử lại sau.'; });

  const form = document.getElementById('leadForm');
  const status = document.getElementById('leadFormStatus');
  const submit = form.querySelector('[type="submit"]');
  let pending = false;
  document.querySelectorAll('[data-need]').forEach(link => link.addEventListener('click', () => {
    form.elements.need.value = link.dataset.need;
  }));
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (pending) return;
    if (!form.checkValidity()) { form.reportValidity(); return; }
    const data = new FormData(form);
    const query = new URLSearchParams(window.location.search);
    const payload = {
      fullName: data.get('fullName'), phone: data.get('phone'), email: data.get('email') || null,
      company: data.get('company') || null, industry: 'Bất động sản', need: data.get('need'),
      message: data.get('message') || null, sourceUrl: window.location.href, referrerUrl: document.referrer || '',
      utmSource: query.get('utm_source'), utmMedium: query.get('utm_medium'), utmCampaign: query.get('utm_campaign'),
      utmContent: query.get('utm_content'), utmTerm: query.get('utm_term'),
      consentAccepted: data.get('consentAccepted') === 'on', website: data.get('website') || ''
    };
    pending = true;
    submit.disabled = true;
    status.className = '';
    status.textContent = 'Đang gửi yêu cầu...';
    try {
      const response = await fetch('/api/contact-leads', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok || !result.success || !result.leadCode) {
        const errors = result.errors && Object.values(result.errors).flat().join(' ');
        throw new Error(response.status === 429 ? 'Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau.' :
          response.status === 503 ? 'Dịch vụ tạm thời không khả dụng. Vui lòng thử lại sau.' :
          response.status === 400 ? errors || 'Thông tin chưa hợp lệ. Vui lòng kiểm tra lại.' :
          'Chưa gửi được thông tin. Vui lòng thử lại sau.');
      }
      status.className = 'success';
      status.textContent = result.message || 'TodoX đã nhận thông tin của bạn.';
      form.reset();
    } catch (error) {
      status.className = 'error';
      status.textContent = error instanceof TypeError ? 'Không thể kết nối. Vui lòng thử lại sau.' : error.message;
    } finally { pending = false; submit.disabled = false; }
  });
})();