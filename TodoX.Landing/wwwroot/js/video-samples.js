(() => {
  const menu = document.querySelector('.menu-toggle');
  const nav = document.getElementById('navigation');
  menu.addEventListener('click', () => {
    const open = menu.getAttribute('aria-expanded') !== 'true';
    menu.setAttribute('aria-expanded', String(open));
    nav.classList.toggle('open', open);
  });

  const filters = document.getElementById('sampleFilters');
  const status = document.getElementById('sampleStatus');
  const grid = document.getElementById('sampleGrid');
  const player = document.getElementById('samplePlayer');
  const playerVideo = document.getElementById('samplePlayerVideo');
  const validMedia = (url, folder) => typeof url === 'string' && new RegExp(`^/media/landing/video-samples/${folder}/[a-f0-9]{32}\\.[a-z0-9]+$`).test(url);
  const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  };

  let groups = [];
  let videos = [];
  let current = '';

  const openPlayer = video => {
    document.getElementById('samplePlayerTitle').textContent = video.title;
    document.getElementById('samplePlayerDescription').textContent = video.description || '';
    player.classList.toggle('is-portrait', video.aspectRatio === '9:16');
    playerVideo.poster = video.posterUrl;
    playerVideo.src = video.videoUrl;
    player.showModal();
    playerVideo.play().catch(() => {});
  };
  const closePlayer = () => { if (player.open) player.close(); };
  document.getElementById('samplePlayerClose').addEventListener('click', closePlayer);
  player.addEventListener('click', event => { if (event.target === player) closePlayer(); });
  player.addEventListener('close', () => { playerVideo.pause(); playerVideo.removeAttribute('src'); playerVideo.load(); });

  const card = video => {
    const article = el('article', video.aspectRatio === '16:9' ? 'vs-card is-landscape' : 'vs-card');
    const thumb = el('button', 'vs-thumb');
    thumb.type = 'button';
    thumb.setAttribute('aria-label', `Xem video ${video.title}`);
    thumb.addEventListener('click', () => openPlayer(video));
    const image = el('img');
    image.src = video.posterUrl;
    image.alt = '';
    image.loading = 'lazy';
    const icon = el('i', 'fa-solid fa-play');
    icon.setAttribute('aria-hidden', 'true');
    thumb.append(image, icon, el('span', 'vs-ratio', video.aspectRatio));
    const body = el('div', 'vs-body');
    body.append(el('span', 'vs-group', video.groupName), el('h2', '', video.title));
    if (video.description) body.append(el('p', '', video.description));
    article.append(thumb, body);
    return article;
  };

  const render = () => {
    filters.querySelectorAll('.vs-filter').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.group === current)));
    grid.replaceChildren(...videos.filter(video => !current || video.groupCode === current).map(card));
  };

  const groupFromUrl = () => {
    const code = new URLSearchParams(window.location.search).get('nhom') || '';
    return groups.some(group => group.code === code) ? code : '';
  };

  const filterButton = (code, name, count) => {
    const button = el('button', 'vs-filter', name);
    button.type = 'button';
    button.dataset.group = code;
    button.append(el('small', '', String(count)));
    button.addEventListener('click', () => {
      current = code;
      history.pushState(null, '', code ? `/mau-video?nhom=${encodeURIComponent(code)}` : '/mau-video');
      render();
    });
    return button;
  };

  window.addEventListener('popstate', () => { current = groupFromUrl(); render(); });

  fetch('/api/landing/video-samples', { credentials: 'same-origin' })
    .then(response => { if (!response.ok) throw new Error('Samples unavailable'); return response.json(); })
    .then(catalog => {
      videos = (Array.isArray(catalog.videos) ? catalog.videos : []).filter(video => video && typeof video.title === 'string' && validMedia(video.videoUrl, 'videos') && validMedia(video.posterUrl, 'posters'));
      groups = (Array.isArray(catalog.groups) ? catalog.groups : []).filter(group => group && typeof group.code === 'string' && group.code && typeof group.name === 'string');
      if (!videos.length) { status.textContent = 'Kho mẫu video đang được cập nhật. Vui lòng quay lại sau.'; return; }
      status.textContent = '';
      filters.replaceChildren(filterButton('', 'Tất cả', videos.length), ...groups.map(group => filterButton(group.code, group.name, group.count)));
      filters.hidden = false;
      current = groupFromUrl();
      render();
    })
    .catch(() => { status.textContent = 'Chưa tải được mẫu video. Vui lòng thử lại sau.'; });
})();
