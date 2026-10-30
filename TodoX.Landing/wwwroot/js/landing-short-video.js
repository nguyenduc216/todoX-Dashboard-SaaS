(() => {
  // Homepage entry points for /video-ngan. Visible by default; the dashboard can hide them or change pricing.
  const plans = document.getElementById('shortVideoTeaserPlans');
  const money = value => new Intl.NumberFormat('vi-VN').format(value) + 'đ';
  fetch('/api/landing/short-video', { credentials: 'same-origin' })
    .then(response => { if (!response.ok) throw new Error('Settings unavailable'); return response.json(); })
    .then(config => {
      if (config.showOnLanding === false) {
        document.querySelectorAll('[data-short-video-entry]').forEach(node => { node.hidden = true; });
        return;
      }
      const packages = Array.isArray(config.packages) ? config.packages.filter(p => p && typeof p.name === 'string' && p.name && Number.isFinite(p.price) && Number.isInteger(p.videoCount) && p.videoCount > 0) : [];
      if (!plans || !packages.length) return;
      plans.replaceChildren(...packages.map(p => {
        const link = document.createElement('a');
        link.className = p.featured ? 'sv-teaser-plan is-featured' : 'sv-teaser-plan';
        link.href = '/video-ngan#pricing';
        const name = document.createElement('b'); name.textContent = p.name;
        const price = document.createElement('strong'); price.textContent = money(p.price);
        const count = document.createElement('span');
        count.textContent = p.bonusVideos > 0 ? `${p.videoCount} video + tặng ${String(p.bonusVideos).padStart(2, '0')}` : `${p.videoCount} video`;
        link.append(name, price, count);
        return link;
      }));
    }).catch(() => {});
})();
