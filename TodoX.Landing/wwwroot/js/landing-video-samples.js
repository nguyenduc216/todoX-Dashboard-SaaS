(() => {
  // Homepage "Mẫu video" menu: lists the groups present in the sample library. The link still works without them.
  const submenu = document.getElementById('videoSampleGroups');
  if (!submenu) return;
  fetch('/api/landing/video-samples', { credentials: 'same-origin' })
    .then(response => { if (!response.ok) throw new Error('Samples unavailable'); return response.json(); })
    .then(catalog => {
      const groups = Array.isArray(catalog.groups) ? catalog.groups.filter(group => group && typeof group.code === 'string' && group.code && typeof group.name === 'string') : [];
      if (!groups.length) return;
      submenu.replaceChildren(...groups.map(group => {
        const link = document.createElement('a');
        link.href = `/mau-video?nhom=${encodeURIComponent(group.code)}`;
        link.textContent = group.name;
        return link;
      }));
      submenu.hidden = false;
    }).catch(() => {});
})();
