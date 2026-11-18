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

  // iPhone/iPad ignore the download attribute for videos and open them in the system player instead of saving.
  // There the "Tải xuống" link fetches the file and opens the share sheet, whose "Lưu video" puts it in Photos.
  const isIos = /iPad|iPhone|iPod/.test(navigator.userAgent)
    || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  const prepared = new WeakMap();

  async function saveOnIos(link) {
    const label = link.querySelector('span');
    const setLabel = text => { if (label) label.textContent = text; };
    let file = prepared.get(link);
    if (!file) {
      if (link.dataset.busy) return;
      link.dataset.busy = '1';
      setLabel('Đang chuẩn bị video…');
      try {
        const response = await fetch(link.href, { credentials: 'same-origin' });
        const type = (response.headers.get('content-type') || '').split(';')[0];
        if (!response.ok || !type.startsWith('video/')) throw new Error('not a video');
        const blob = await response.blob();
        file = new File([blob], link.dataset.fileName || 'todox-video.mp4', { type: blob.type || type });
        prepared.set(link, file);
      } catch {
        setLabel('Tải xuống');
        window.location.href = link.href;
        return;
      } finally {
        delete link.dataset.busy;
      }
    }
    try {
      await navigator.share({ files: [file] });
      setLabel('Tải xuống');
    } catch (error) {
      if (error && error.name === 'AbortError') { setLabel('Tải xuống'); return; }
      // Preparing took too long for iOS to treat the sheet as a reply to the tap: the next tap opens it at once.
      setLabel('Bấm lần nữa để lưu video');
    }
  }

  document.addEventListener('click', event => {
    const link = event.target.closest('a[data-video-download]');
    if (link && isIos && navigator.canShare && navigator.canShare({ files: [new File([''], 'x.mp4', { type: 'video/mp4' })] })) {
      event.preventDefault();
      saveOnIos(link);
      return;
    }
    const button = event.target.closest('[data-share-url]');
    if (!button) return;
    event.preventDefault();
    share(button);
  });
})();
