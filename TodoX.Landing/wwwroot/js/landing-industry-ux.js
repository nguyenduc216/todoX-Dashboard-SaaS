(() => {
  const modal = document.getElementById('industryModal');
  const video = document.getElementById('industryModalVideo');
  const industryWrapper = document.getElementById('industrySolutions');
  if (!modal || !video) return;

  const media = video.closest('.industry-modal__video');
  if (!media) return;

  let overlay = media.querySelector('.industry-modal-play-overlay');
  if (!overlay) {
    overlay = document.createElement('button');
    overlay.type = 'button';
    overlay.className = 'industry-modal-play-overlay';
    overlay.setAttribute('aria-label', 'Phát video');
    overlay.innerHTML = '<i class="fa-solid fa-play"></i>';
    media.appendChild(overlay);
  }

  const syncOverlay = () => {
    const modalOpen = modal.classList.contains('is-open');
    const hasSource = Boolean(video.currentSrc || video.getAttribute('src'));
    const show = modalOpen && hasSource && (video.paused || video.ended);
    overlay.classList.toggle('is-hidden', !show);
  };

  overlay.addEventListener('click', async () => {
    try {
      video.muted = false;
      await video.play();
    } catch {
      // Browser may still require native controls; keep overlay visible.
    }
    syncOverlay();
  });

  ['play', 'playing'].forEach(name => video.addEventListener(name, syncOverlay));
  ['pause', 'ended', 'loadedmetadata', 'canplay', 'emptied'].forEach(name => video.addEventListener(name, syncOverlay));

  const decorateIndustryCards = (root = document) => {
    root.querySelectorAll?.('.industry-card:not(.is-disabled)').forEach(card => {
      if (card.querySelector('.industry-card-play-overlay')) return;
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'industry-card-play-overlay';
      button.setAttribute('aria-label', 'Xem video demo');
      button.innerHTML = '<i class="fa-solid fa-play"></i>';
      card.appendChild(button);
    });
  };

  decorateIndustryCards();

  if (industryWrapper) {
    const cardObserver = new MutationObserver(() => decorateIndustryCards(industryWrapper));
    cardObserver.observe(industryWrapper, { childList: true, subtree: true });
  }

  const observer = new MutationObserver(syncOverlay);
  observer.observe(modal, { attributes: true, attributeFilter: ['class', 'aria-hidden'] });
  observer.observe(video, { attributes: true, attributeFilter: ['src', 'poster'] });

  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') window.setTimeout(syncOverlay, 0);
  });

  syncOverlay();
})();
