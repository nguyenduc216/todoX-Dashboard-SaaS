(() => {
  const endpoint = (window.TODOX_LANDING_CONFIG || {}).industryEndpoint || '/api/industry-solutions';
  const wrapper = document.getElementById('industrySolutions');
  const modal = document.getElementById('industryModal');
  const videoEl = document.getElementById('industryModalVideo');
  const titleEl = document.getElementById('industryModalTitle');
  const descriptionEl = document.getElementById('industryModalDescription');
  const notesEl = document.getElementById('industryModalNotes');
  if (!wrapper || !modal || !videoEl || !titleEl || !descriptionEl || !notesEl) return;

  let industries = [];
  let activeIndustry = null;
  let activeVideoId = null;

  const escapeHtml = value => String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');

  const stopVideo = () => {
    try { videoEl.pause(); } catch {}
    try {
      videoEl.muted = true;
      videoEl.currentTime = 0;
      videoEl.removeAttribute('src');
      videoEl.load();
    } catch {}
  };

  async function ensureData() {
    if (industries.length) return industries;
    try {
      const response = await fetch(endpoint, { headers: { Accept: 'application/json' } });
      const data = response.ok ? await response.json() : [];
      industries = Array.isArray(data) ? data : [];
    } catch {
      industries = [];
    }
    return industries;
  }

  function ensureRelatedContainer() {
    let container = modal.querySelector('.industry-related-videos');
    if (container) return container;

    container = document.createElement('div');
    container.className = 'industry-related-videos';
    const consult = modal.querySelector('[data-industry-consult]');
    if (consult?.parentElement) {
      consult.parentElement.insertBefore(container, consult);
    } else {
      notesEl.insertAdjacentElement('afterend', container);
    }
    return container;
  }

  function renderNotes(item) {
    const pairs = [
      ['Định dạng phù hợp', item?.formatNote],
      ['Mục tiêu', item?.goalNote],
      ['TodoX có thể triển khai', item?.capabilityNote]
    ].filter(([, value]) => value);
    notesEl.innerHTML = pairs.map(([label, value]) =>
      `<div class="industry-modal__note"><strong>${escapeHtml(label)}</strong>${escapeHtml(value)}</div>`
    ).join('');
  }

  function renderRelated() {
    const container = ensureRelatedContainer();
    const list = (activeIndustry?.videos || []).filter(v => v && v.videoUrl && v.id !== activeVideoId);
    if (!list.length) {
      container.hidden = true;
      container.innerHTML = '';
      return;
    }

    container.hidden = false;
    container.innerHTML = `
      <div class="industry-related-videos__head">
        <h4>Video cùng ngành nghề</h4>
        <span class="industry-related-videos__count">${list.length} video khác</span>
      </div>
      <div class="industry-related-videos__grid">
        ${list.map(item => `
          <button type="button" class="industry-related-video" data-related-video-id="${escapeHtml(item.id)}">
            <span class="industry-related-video__media">
              ${item.thumbnailUrl
                ? `<img src="${escapeHtml(item.thumbnailUrl)}" alt="${escapeHtml(item.title || activeIndustry.title)}" loading="lazy">`
                : '<span class="industry-related-video__placeholder">TODOX</span>'}
              <span class="industry-related-video__play"><i class="fa-solid fa-play"></i></span>
            </span>
            <span class="industry-related-video__body">
              <span class="industry-related-video__title">${escapeHtml(item.title || activeIndustry.title)}</span>
              <span class="industry-related-video__desc">${escapeHtml(item.shortDescription || item.description || '')}</span>
            </span>
          </button>`).join('')}
      </div>`;
  }

  function applyVideo(item, industry) {
    if (!item?.videoUrl || !industry) return;
    activeIndustry = industry;
    activeVideoId = item.id || null;

    stopVideo();
    titleEl.textContent = item.title || industry.title || '';
    descriptionEl.textContent = item.description || item.shortDescription || industry.description || industry.shortDescription || '';
    renderNotes(item);

    if (item.thumbnailUrl) videoEl.poster = item.thumbnailUrl;
    else if (industry.thumbnailUrl) videoEl.poster = industry.thumbnailUrl;
    else videoEl.removeAttribute('poster');

    videoEl.muted = false;
    videoEl.src = item.videoUrl;
    videoEl.load();

    const dialog = modal.querySelector('.industry-modal__dialog');
    const landscape = item.aspectRatio === '16:9';
    dialog?.classList.toggle('is-landscape', landscape);
    dialog?.classList.toggle('is-portrait', !landscape);
    renderRelated();
  }

  function enhanceIndustry(industry) {
    if (!industry || !modal.classList.contains('is-open')) return;
    const videos = Array.isArray(industry.videos) ? industry.videos.filter(v => v?.videoUrl) : [];
    if (!videos.length) return;
    const primary = videos.find(v => v.isPrimary) || videos[0];
    applyVideo(primary, industry);
  }

  async function handleCard(event) {
    const card = event.target.closest('.industry-card[data-industry-index]');
    if (!card) return;
    const items = await ensureData();
    const index = Number(card.dataset.industryIndex);
    const industry = Number.isInteger(index) ? items[index] : null;
    if (!industry) return;
    window.setTimeout(() => enhanceIndustry(industry), 30);
  }

  wrapper.addEventListener('click', handleCard);
  wrapper.addEventListener('keydown', event => {
    if (event.key === 'Enter' || event.key === ' ') handleCard(event);
  });

  modal.addEventListener('click', event => {
    const related = event.target.closest('[data-related-video-id]');
    if (!related || !activeIndustry) return;
    event.preventDefault();
    const selected = (activeIndustry.videos || []).find(v => String(v.id) === related.dataset.relatedVideoId);
    if (selected) applyVideo(selected, activeIndustry);
  });

  modal.querySelectorAll('[data-industry-modal-close]').forEach(el => {
    el.addEventListener('click', () => {
      activeIndustry = null;
      activeVideoId = null;
      const related = modal.querySelector('.industry-related-videos');
      if (related) related.innerHTML = '';
    });
  });

  ensureData();
})();
