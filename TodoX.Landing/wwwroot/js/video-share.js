(() => {
  // Shared by the /video/{slug} page and the landing video modal:
  // any [data-share-url] button opens the native share sheet or copies the link.
  async function share(button) {
    const url = button.dataset.shareUrl;
    if (!url) return;
    const absolute = new URL(url, window.location.origin).toString();
    const label = button.querySelector('span');
    const original = label ? label.textContent : '';

    if (navigator.share && window.matchMedia('(pointer: coarse)').matches) {
      try {
        await navigator.share({ title: button.dataset.shareTitle || document.title, url: absolute });
        return;
      } catch (error) {
        if (error && error.name === 'AbortError') return;
      }
    }

    try {
      await navigator.clipboard.writeText(absolute);
    } catch {
      window.prompt('Sao chép link video:', absolute);
      return;
    }
    if (label) {
      label.textContent = 'Đã sao chép link';
      window.setTimeout(() => { label.textContent = original; }, 2000);
    }
  }

  document.addEventListener('click', event => {
    const button = event.target.closest('[data-share-url]');
    if (!button) return;
    event.preventDefault();
    share(button);
  });
})();
