(() => {
  const API = (window.TODOX_LANDING_CONFIG && window.TODOX_LANDING_CONFIG.industryEndpoint) || '/api/industry-solutions';
  let industries = [];
  let currentIndustry = null;

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));

  async function loadIndustryMap() {
    try {
      const response = await fetch(API, { headers: { Accept: 'application/json' }, cache: 'no-store' });
      if (!response.ok) return;
      const data = await response.json();
      industries = Array.isArray(data) ? data : [];
    } catch {
      industries = [];
    }
  }

  function findIndustryForCard(card) {
    const title = (card?.dataset?.title || $('.industry-content h3', card)?.textContent || '').trim();
    return industries.find(x => String(x.title || '').trim().toLowerCase() === title.toLowerCase()) || {
      title,
      shortDescription: $('.industry-content p', card)?.textContent?.trim() || '',
      description: card?.dataset?.description || '',
      videoUrl: card?.dataset?.video || '',
      aspectRatio: card?.dataset?.aspect || '9:16',
      thumbnailUrl: $('img', card)?.getAttribute('src') || ''
    };
  }

  function applyVideoPresentation(item) {
    // landing-patch.js creates a second video element. The original HTML also contains
    // #industryModalVideo, so selecting by global id can target the hidden/legacy player.
    // Always scope the player lookup to the currently visible dynamic media container.
    window.setTimeout(() => {
      const media = $('#industryVideoMedia');
      const video = media ? $('video', media) : null;
      if (!video || !media) return;

      // Stop any legacy/hidden player that may have received the source because of duplicate IDs.
      $$('#industryModalVideo').forEach(legacy => {
        if (legacy !== video) {
          try { legacy.pause(); } catch {}
          legacy.removeAttribute('src');
          try { legacy.load(); } catch {}
        }
      });

      const videoUrl = String(item?.videoUrl || '').trim();
      if (videoUrl) {
        if (video.getAttribute('src') !== videoUrl) {
          video.src = videoUrl;
          video.load();
        }
      }

      if (item?.thumbnailUrl) video.poster = item.thumbnailUrl;
      video.style.width = '100%';
      video.style.height = '100%';
      video.style.objectFit = 'contain';
      video.style.background = '#050607';

      let warning = $('.industry-codec-warning', media);
      if (!warning) {
        warning = document.createElement('div');
        warning.className = 'industry-codec-warning';
        warning.hidden = true;
        warning.innerHTML = '<strong>Không thể hiển thị hình video.</strong><span>TodoX đã tải đúng player nhưng trình duyệt chưa giải mã được video. Hãy kiểm tra file H.264/AAC hoặc đường dẫn media.</span>';
        media.appendChild(warning);
      }

      const verifyVideoTrack = () => {
        if (video.readyState >= 1 && video.videoWidth === 0 && video.videoHeight === 0) {
          warning.hidden = false;
        } else {
          warning.hidden = true;
        }
      };

      video.addEventListener('loadedmetadata', verifyVideoTrack, { once: true });
      video.addEventListener('canplay', verifyVideoTrack, { once: true });
      video.addEventListener('error', () => { warning.hidden = false; }, { once: true });

      // Start playback on the visible player only. Browsers may still require user gesture;
      // this call happens immediately after the user's card click.
      if (videoUrl) video.play().catch(() => {});
    }, 0);
  }

  function ensureIndustryOption(select, title) {
    if (!select || !title) return;
    const existing = Array.from(select.options).find(o => o.text.trim().toLowerCase() === title.trim().toLowerCase());
    if (existing) {
      select.value = existing.value;
      return;
    }
    const option = new Option(title, title, true, true);
    select.add(option);
  }

  function goToConsultation(item) {
    const form = $('#leadForm');
    const contact = $('#contact');
    if (!form || !contact) return;

    const industrySelect = form.querySelector('select[name="industry"]');
    const needSelect = form.querySelector('select[name="need"]');
    const message = form.querySelector('textarea[name="message"]');

    ensureIndustryOption(industrySelect, item?.title || '');
    if (industrySelect) industrySelect.dispatchEvent(new Event('change', { bubbles: true }));

    if (needSelect) {
      const aiVideo = Array.from(needSelect.options).find(o => o.text.trim().toLowerCase() === 'ai video');
      if (aiVideo) needSelect.value = aiVideo.value;
      needSelect.dispatchEvent(new Event('change', { bubbles: true }));
    }

    if (message && !message.value.trim()) {
      message.value = `Tôi cần tư vấn giải pháp Video AI cho ngành ${item?.title || ''}.`.trim();
      message.dispatchEvent(new Event('input', { bubbles: true }));
    }

    const modal = $('#industryVideoModal');
    const visiblePlayer = $('#industryVideoMedia video');
    try { visiblePlayer?.pause(); } catch {}
    modal?.classList.remove('is-open');
    modal?.setAttribute('aria-hidden', 'true');
    document.body.classList.remove('modal-open');

    const headerHeight = $('#header')?.offsetHeight || 78;
    window.setTimeout(() => {
      const top = Math.max(0, contact.offsetTop - headerHeight - 12);
      window.scrollTo({ top, behavior: 'smooth' });
      window.setTimeout(() => form.querySelector('input[name="fullName"]')?.focus({ preventScroll: true }), 500);
    }, 30);
  }

  function bindFixes() {
    document.addEventListener('click', (event) => {
      const card = event.target.closest('.industry-card-v2');
      if (!card) return;
      currentIndustry = findIndustryForCard(card);
      applyVideoPresentation(currentIndustry);
    }, true);

    document.addEventListener('click', (event) => {
      const button = event.target.closest('#industryVideoModal a.btn[href="#contact"], #industryModal a.btn[href="#contact"]');
      if (!button) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      goToConsultation(currentIndustry || {});
    }, true);
  }

  function injectStyles() {
    if ($('#landing-industry-fix-style')) return;
    const style = document.createElement('style');
    style.id = 'landing-industry-fix-style';
    style.textContent = `
      .industry-video-media{position:relative;overflow:hidden;background:#050607}
      .industry-video-media video{display:block;width:100%;height:100%;object-fit:contain;background:#050607}
      .industry-codec-warning{position:absolute;inset:auto 16px 16px 16px;z-index:5;padding:12px 14px;border:1px solid rgba(255,189,34,.42);border-radius:12px;background:rgba(8,10,13,.94);color:#f4f5f7;box-shadow:0 12px 32px rgba(0,0,0,.4)}
      .industry-codec-warning[hidden]{display:none!important}
      .industry-codec-warning strong,.industry-codec-warning span{display:block}
      .industry-codec-warning strong{color:#ffbd22;margin-bottom:4px;font-size:14px}
      .industry-codec-warning span{color:#b8bdc7;font-size:12px;line-height:1.45}
    `;
    document.head.appendChild(style);
  }

  const boot = async () => {
    injectStyles();
    await loadIndustryMap();
    bindFixes();
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();
})();
