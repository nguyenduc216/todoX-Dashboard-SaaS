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

  const form = document.getElementById('leadForm');
  const needSelect = document.getElementById('needSelect');
  const bindNeedLinks = root => root.querySelectorAll('[data-need]').forEach(link => link.addEventListener('click', () => {
    if ([...needSelect.options].some(option => option.value === link.dataset.need)) needSelect.value = link.dataset.need;
  }));

  // Pricing: the HTML holds the default packages; the dashboard can override them.
  const money = value => new Intl.NumberFormat('vi-VN').format(value) + 'đ';
  const pad = value => String(value).padStart(2, '0');
  const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  };
  const icon = className => { const i = el('i', className); i.setAttribute('aria-hidden', 'true'); return i; };
  const renderPackages = packages => {
    const grid = document.getElementById('pricingGrid');
    const cards = packages.map(p => {
      const card = el('article', p.featured ? 'sv-plan is-featured' : 'sv-plan');
      if (p.featured) { const badge = el('span', 'sv-badge'); badge.append(icon('fa-solid fa-crown'), ' GÓI NÊN MUA'); card.append(badge); }
      const header = el('header');
      header.append(el('h3', '', p.name));
      if (p.subtitle) header.append(el('p', '', p.subtitle));
      const count = el('div', 'sv-count');
      const countText = el('div');
      countText.append(el('b', '', `${p.videoCount} video`), el('small', '', 'HD 1080 · 30 – 40 giây'));
      count.append(icon('fa-solid fa-film'), countText);
      card.append(header, el('div', 'sv-price', money(p.price)), count);
      if (p.bonusVideos > 0) { const gift = el('div', 'sv-gift'); gift.append(icon('fa-solid fa-gift'), `Tặng ${pad(p.bonusVideos)} video`); card.append(gift); }
      const list = el('ul');
      list.append(el('li', '', `Tổng ${p.videoCount + p.bonusVideos} video`));
      (p.features || []).forEach(feature => list.append(el('li', '', feature)));
      const cta = el('a', 'sv-plan-cta', 'Chọn gói này');
      cta.href = '#consultation';
      cta.dataset.need = p.name;
      card.append(list, cta);
      return card;
    });
    grid.replaceChildren(...cards);
    const keep = [...needSelect.options].filter(option => option.value === '' || option.value === 'Cần tư vấn thêm');
    needSelect.replaceChildren(keep[0], ...packages.map(p => new Option(p.name, p.name)), ...keep.slice(1));
    bindNeedLinks(grid);
  };
  bindNeedLinks(document);

  const video = document.getElementById('demoVideo');
  const frame = document.getElementById('demoFrame');
  const play = document.getElementById('demoPlay');
  const sound = document.getElementById('demoSound');
  const fullscreen = document.getElementById('demoFullscreen');
  const hint = document.getElementById('demoHint');
  const validMedia = url => typeof url === 'string' && /^\/media\/landing\/short-video\/(videos|posters)\/[a-zA-Z0-9_.%-]+$/.test(url);
  let selectedMuted = null;
  let settings;
  let state = 'poster';
  const setState = next => {
    state = next;
    frame.classList.toggle('is-playing', next === 'playing');
    play.hidden = next === 'playing';
    const label = next === 'ended' ? 'Phát lại video' : next === 'paused' ? 'Tiếp tục video' : 'Phát video';
    play.setAttribute('aria-label', label);
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
  const setupIntro = config => {
    if (!config || !config.enabled || !validMedia(config.videoUrl) || !['AUTOPLAY_LOOP', 'AUTOPLAY_ONCE', 'CLICK_TO_PLAY'].includes(config.playbackMode)) {
      play.hidden = true;
      return;
    }
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
    video.addEventListener('error', () => { video.removeAttribute('src'); frame.classList.remove('has-poster'); sound.hidden = true; fullscreen.hidden = true; hint.textContent = 'Video hiện chưa sẵn sàng. Vui lòng thử lại sau.'; setState('poster'); play.hidden = true; });
    if (config.playbackMode !== 'CLICK_TO_PLAY' && !window.matchMedia('(prefers-reduced-motion: reduce)').matches && !navigator.connection?.saveData) start(false);
  };

  fetch('/api/landing/short-video', { credentials: 'same-origin' })
    .then(response => { if (!response.ok) throw new Error('Settings unavailable'); return response.json(); })
    .then(config => {
      const packages = Array.isArray(config.packages) ? config.packages.filter(p => p && typeof p.name === 'string' && p.name && Number.isFinite(p.price) && Number.isInteger(p.videoCount) && p.videoCount > 0 && Number.isInteger(p.bonusVideos) && p.bonusVideos >= 0) : [];
      if (packages.length) renderPackages(packages);
      setupIntro(config.intro);
    }).catch(() => { play.hidden = true; });

  const status = document.getElementById('leadFormStatus');
  const submit = form.querySelector('[type="submit"]');
  let pending = false;
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (pending) return;
    if (!form.checkValidity()) { form.reportValidity(); return; }
    const data = new FormData(form);
    const query = new URLSearchParams(window.location.search);
    const payload = {
      fullName: data.get('fullName'), phone: data.get('phone'), email: data.get('email') || null,
      company: data.get('company') || null, industry: 'Video truyền thông', need: data.get('need'),
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
