(() => {
  // Homepage "Mẫu video" section: the newest samples, each card linking to its own page (/mau-video/{slug}).
  // The full library stays at /mau-video; this section only previews it.
  const grid = document.getElementById('homeSampleGrid');
  const status = document.getElementById('homeSampleStatus');
  const filters = document.getElementById('homeSampleFilters');
  if (!grid || !status || !filters) return;

  const MAX_CARDS = 8;
  const validSharePath = path => typeof path === 'string' && /^\/mau-video\/[a-z0-9-]+$/.test(path);
  const validMedia = (url, folder) => typeof url === 'string' && new RegExp(`^/media/landing/video-samples/${folder}/[a-f0-9]{32}\\.[a-z0-9]+$`).test(url);
  const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  };

  let videos = [];
  let current = '';

  const card = video => {
    const link = el('a', video.aspectRatio === '16:9' ? 'ls-card is-landscape' : 'ls-card');
    link.href = video.sharePath;
    const thumb = el('span', 'ls-thumb');
    const image = el('img');
    image.src = video.posterUrl;
    image.alt = '';
    image.loading = 'lazy';
    const icon = el('i', 'fa-solid fa-play');
    icon.setAttribute('aria-hidden', 'true');
    thumb.append(image, icon, el('span', 'ls-ratio', video.aspectRatio || ''));
    const body = el('span', 'ls-body');
    body.append(el('span', 'ls-group', video.groupName || 'Mẫu video'), el('span', 'ls-name', video.title));
    link.append(thumb, body);
    return link;
  };

  const render = () => {
    filters.querySelectorAll('.ls-filter').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.group === current)));
    grid.replaceChildren(...videos.filter(video => !current || video.groupCode === current).slice(0, MAX_CARDS).map(card));
  };

  const filterButton = (code, name) => {
    const button = el('button', 'ls-filter', name);
    button.type = 'button';
    button.dataset.group = code;
    button.addEventListener('click', () => { current = code; render(); });
    return button;
  };

  fetch('/api/landing/video-samples', { credentials: 'same-origin' })
    .then(response => { if (!response.ok) throw new Error('Samples unavailable'); return response.json(); })
    .then(catalog => {
      videos = (Array.isArray(catalog.videos) ? catalog.videos : []).filter(video =>
        video && typeof video.title === 'string' && validSharePath(video.sharePath)
        && validMedia(video.posterUrl, 'posters') && validMedia(video.videoUrl, 'videos'));
      if (!videos.length) {
        status.textContent = 'Kho mẫu video đang được cập nhật. Bạn xem trước các giải pháp video theo ngành nghề nhé.';
        return;
      }
      status.textContent = '';
      const groups = (Array.isArray(catalog.groups) ? catalog.groups : [])
        .filter(group => group && typeof group.code === 'string' && group.code && typeof group.name === 'string')
        .filter(group => videos.some(video => video.groupCode === group.code));
      if (groups.length > 1) {
        filters.replaceChildren(filterButton('', 'Tất cả'), ...groups.map(group => filterButton(group.code, group.name)));
        filters.hidden = false;
      }
      render();
    })
    .catch(() => {
      status.textContent = 'Kho mẫu video tạm thời chưa tải được. Bạn vui lòng quay lại sau ít phút.';
    });
})();
