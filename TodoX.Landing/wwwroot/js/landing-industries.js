(() => {
  const ready = (fn) => document.readyState === 'loading'
    ? document.addEventListener('DOMContentLoaded', fn, { once: true })
    : fn();

  ready(() => {
    const $ = (selector, root = document) => root.querySelector(selector);
    const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];

    // Header: keep the primary CTA, remove Dashboard and duplicate Contact menu.
    const nav = $('#navLinks');
    if (nav) {
      nav.innerHTML = `
        <a href="#home" class="active" aria-current="page">Trang chủ</a>
        <a href="#about">Giới thiệu</a>
        <a href="#solutions">Giải pháp</a>
        <a href="#aiWorkflow">Quy trình</a>
        <a href="#industries">Ngành nghề</a>
        <a href="#chatstaff">ChatStaff</a>`;
    }
    $$('.nav-actions [data-dashboard-link]').forEach(el => el.remove());

    // Hero content.
    const hero = $('#home');
    if (hero) {
      const eyebrow = $('.hero-copy .eyebrow', hero);
      const h1 = $('.hero-copy h1', hero);
      const intro = $('.hero-copy > p', hero);
      if (eyebrow) eyebrow.innerHTML = '<i class="fa-solid fa-wand-magic-sparkles"></i> TodoX AI Automation';
      if (h1) h1.innerHTML = 'TodoX AI Automation <span class="gold-text">– hệ thống sản xuất video AI tự động giúp bạn xây kênh và affiliate đa nền tảng</span>';
      if (intro) intro.textContent = 'TodoX giúp bạn tự động hóa quá trình sản xuất Video AI, xây kênh triệu view, tối ưu chuyển đổi và mở rộng nội dung bán hàng trên nhiều nền tảng.';
      const stats = $('.stats', hero);
      if (stats) stats.innerHTML = `
        <div class="stat"><strong data-count="100">100+</strong><span>Dự án chuyển đổi</span></div>
        <div class="stat"><strong data-count="20">20+</strong><span>Ngành triển khai</span></div>
        <div class="stat"><strong>1M+</strong><span>Kênh & video triệu view</span></div>
        <div class="stat"><strong>24/7</strong><span>Affiliate đa nền tảng</span></div>`;
      const tag = $('.mini-tag', hero);
      const phoneTitle = $('.phone-content h3', hero);
      const phoneText = $('.phone-content p', hero);
      if (tag) tag.textContent = 'CHUYÊN GIA AI';
      if (phoneTitle) phoneTitle.textContent = 'Xây kênh triệu view chuyển đổi cao bằng Video AI';
      if (phoneText) phoneText.textContent = 'Triển khai 100+ dự án, 20+ ngành và hệ thống affiliate đa nền tảng.';
    }

    // Founder copy only. Founder photo stays managed by landing-config.js.
    const founder = $('#founder');
    if (founder) {
      const sub = $('.founder-copy h3', founder);
      const p = $('.founder-copy p', founder);
      const metrics = $('.founder-metrics', founder);
      if (sub) sub.textContent = 'Chuyên gia Affiliate, Video ngắn & Xây kênh bán hàng';
      if (p) p.textContent = 'Hơn 6 năm kinh nghiệm kinh doanh online đa nền tảng, chuyên gia affiliate, sản xuất video ngắn xây kênh và bán hàng, anh Trần Trọng Tuyên tập trung xây dựng các quy trình nội dung có khả năng tăng chuyển đổi, mở rộng quy mô và tạo giá trị thực tế cho doanh nghiệp.';
      if (metrics) metrics.innerHTML = `
        <div class="metric"><strong>6+</strong><span>Năm kinh nghiệm</span></div>
        <div class="metric"><strong>100+</strong><span>Dự án triển khai</span></div>
        <div class="metric"><strong>20+</strong><span>Ngành hàng</span></div>
        <div class="metric"><strong>1M+</strong><span>Lượt xem chuyển đổi</span></div>`;
    }

    // The previous #solutions section is the Workflow. Give it its real id and add a true Solutions section.
    let workflow = $('.ai-workflow-section');
    if (workflow) {
      workflow.id = 'aiWorkflow';
      const workflowShell = $('.ai-workflow-shell', workflow);
      if (workflowShell) workflowShell.id = 'workflowShell';
    }

    if (!$('#solutions')) {
      const solutions = document.createElement('section');
      solutions.className = 'section';
      solutions.id = 'solutions';
      solutions.innerHTML = `
        <div class="container">
          <div class="section-title" data-aos="fade-up">
            <span class="eyebrow">Giải pháp TodoX</span>
            <h2>Từ ý tưởng đến nội dung bán hàng <span class="gold-text">có thể vận hành</span></h2>
            <p>TodoX kết hợp AI, automation và dữ liệu để xây dựng hệ thống sản xuất nội dung có thể mở rộng theo từng mô hình kinh doanh.</p>
          </div>
          <div class="solutions-grid">
            <article class="glass feature-card"><div class="icon-box"><i class="fa-solid fa-wand-magic-sparkles"></i></div><h3>AI Video</h3><p>Video bán hàng, social video, UGC, review sản phẩm, timelapse và nội dung theo từng ngành nghề.</p><a class="solution-link" href="#industries">Xem theo ngành →</a></article>
            <article class="glass feature-card"><div class="icon-box"><i class="fa-solid fa-gears"></i></div><h3>Automation Content</h3><p>Chuẩn hóa pipeline từ dữ liệu đầu vào, tạo nội dung, kiểm duyệt đến xuất bản đa kênh.</p><a class="solution-link" href="#aiWorkflow">Xem quy trình →</a></article>
            <article class="glass feature-card"><div class="icon-box"><i class="fa-solid fa-comments"></i></div><h3>ChatStaff</h3><p>Chatbot AI hỗ trợ tư vấn, tiếp nhận nhu cầu và tự động hóa tương tác khách hàng.</p><a class="solution-link" href="#chatstaff">Khám phá ChatStaff →</a></article>
            <article class="glass feature-card"><div class="icon-box"><i class="fa-solid fa-chart-line"></i></div><h3>Growth System</h3><p>Kết hợp nội dung, landing page và quy trình chăm sóc để tối ưu chuyển đổi thực tế.</p><a class="solution-link" href="#contact">Nhận tư vấn →</a></article>
          </div>
        </div>`;
      if (workflow) workflow.before(solutions);
      else $('#industries')?.before(solutions);
    }

    // Keep section order aligned with the header menu.
    const about = $('#about');
    workflow = $('#aiWorkflow');
    const industries = $('#industries');
    const solutions = $('#solutions');
    if (about && founder && solutions && workflow && industries) {
      about.after(founder);
      founder.after(solutions);
      solutions.after(workflow);
      workflow.after(industries);
    }

    // Contact + footer current business information.
    $$('#contact p, footer span').forEach(el => {
      if (el.textContent.includes('0909 123 456')) el.innerHTML = el.innerHTML.replace('0909 123 456', '0366 699 961');
    });
    const copyright = $('.copyright');
    if (copyright) copyright.textContent = '© 2026 TodoX.';
    const footerSolutionLinks = $$('.footer-grid > div').find(x => $('h3', x)?.textContent.trim() === 'Giải pháp');
    if (footerSolutionLinks) {
      const links = $('.footer-links', footerSolutionLinks);
      if (links && !links.querySelector('[data-dashboard-link]')) {
        links.insertAdjacentHTML('beforeend', '<a href="https://dashboard.todox.vn" data-dashboard-link><i class="fa-solid fa-right-to-bracket"></i> Đăng nhập Dashboard</a>');
      }
    }

    // Industry section is rendered from database data exposed by TodoX.Landing.
    const industrySection = $('#industries');
    if (industrySection) {
      const oldSwiper = $('.industrySwiper', industrySection);
      const holder = document.createElement('div');
      holder.className = 'industry-dynamic';
      holder.innerHTML = '<div class="industry-grid" id="industryGrid"><div class="industry-empty">Đang tải giải pháp ngành nghề...</div></div>';
      oldSwiper?.replaceWith(holder);
      loadIndustries();
    }

    ensureVideoModal();
    bindNavigation();

    async function loadIndustries() {
      const grid = $('#industryGrid');
      if (!grid) return;
      try {
        const response = await fetch('/api/industry-solutions', { headers: { Accept: 'application/json' } });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const items = await response.json();
        renderIndustries(Array.isArray(items) ? items : []);
      } catch (error) {
        console.warn('TodoX industry data unavailable', error);
        renderIndustries([]);
      }
    }

    function renderIndustries(items) {
      const grid = $('#industryGrid');
      if (!grid) return;
      if (!items.length) {
        grid.innerHTML = '<div class="industry-empty">Nội dung ngành nghề đang được cập nhật.</div>';
        return;
      }
      grid.innerHTML = items.map(item => `
        <article class="industry-card-v2" role="button" tabindex="0"
          data-title="${esc(item.title)}" data-description="${esc(item.description || item.shortDescription || '')}"
          data-video="${esc(item.videoUrl || '')}" data-aspect="${item.aspectRatio === '16:9' ? '16:9' : '9:16'}">
          <img src="${esc(item.thumbnailUrl || '/img/landing/interior.jpg')}" alt="${esc(item.title)}" loading="lazy">
          <span class="video-meta">${item.aspectRatio === '16:9' ? '16:9' : '9:16'} · VIDEO AI</span>
          ${item.videoUrl ? '<span class="video-play-badge"><i class="fa-solid fa-play"></i></span>' : ''}
          <div class="industry-content"><h3>${esc(item.title)}</h3><p>${esc(item.shortDescription || '')}</p></div>
        </article>`).join('');

      $$('.industry-card-v2', grid).forEach(card => {
        card.addEventListener('click', () => openIndustry(card));
        card.addEventListener('keydown', e => {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openIndustry(card); }
        });
      });
    }

    function ensureVideoModal() {
      if ($('#industryVideoModal')) return;
      document.body.insertAdjacentHTML('beforeend', `
        <div class="industry-video-modal" id="industryVideoModal" aria-hidden="true">
          <div class="industry-video-dialog" id="industryVideoDialog" role="dialog" aria-modal="true">
            <button class="industry-video-close" id="industryVideoClose" aria-label="Đóng"><i class="fa-solid fa-xmark"></i></button>
            <div class="industry-video-layout">
              <div class="industry-video-media" id="industryVideoMedia"><video id="industryModalVideo" controls playsinline preload="metadata"></video></div>
              <div class="industry-video-details"><div><div class="industry-video-tag">TodoX Industry Solution</div><h3 id="industryVideoTitle"></h3><p id="industryVideoDescription"></p></div><a class="btn btn-primary" href="#contact">Tư vấn giải pháp này <i class="fa-solid fa-arrow-right"></i></a></div>
            </div>
          </div>
        </div>`);
      $('#industryVideoClose')?.addEventListener('click', closeIndustry);
      $('#industryVideoModal')?.addEventListener('click', e => { if (e.target.id === 'industryVideoModal') closeIndustry(); });
      document.addEventListener('keydown', e => { if (e.key === 'Escape') closeIndustry(); });
    }

    function openIndustry(card) {
      ensureVideoModal();
      const modal = $('#industryVideoModal');
      const dialog = $('#industryVideoDialog');
      const media = $('#industryVideoMedia');
      const video = $('#industryModalVideo');
      $('#industryVideoTitle').textContent = card.dataset.title || '';
      $('#industryVideoDescription').textContent = card.dataset.description || '';
      dialog.classList.toggle('is-landscape', card.dataset.aspect === '16:9');
      dialog.classList.toggle('is-portrait', card.dataset.aspect !== '16:9');
      if (card.dataset.video) {
        media.innerHTML = '<video id="industryModalVideo" controls playsinline preload="metadata"></video>';
        const player = $('#industryModalVideo');
        player.src = card.dataset.video;
        player.play().catch(() => {});
      } else {
        media.innerHTML = '<div class="industry-video-placeholder"><div><i class="fa-solid fa-video-slash fa-2x"></i><p>Video đang được cập nhật.</p></div></div>';
      }
      modal.classList.add('is-open');
      modal.setAttribute('aria-hidden', 'false');
      document.body.classList.add('modal-open');
    }

    function closeIndustry() {
      const modal = $('#industryVideoModal');
      const video = $('#industryModalVideo');
      video?.pause();
      modal?.classList.remove('is-open');
      modal?.setAttribute('aria-hidden', 'true');
      document.body.classList.remove('modal-open');
    }

    function bindNavigation() {
      const links = $$('#navLinks a[href^="#"]');
      const setActive = id => links.forEach(link => {
        const active = link.getAttribute('href') === `#${id}`;
        link.classList.toggle('active', active);
        active ? link.setAttribute('aria-current', 'page') : link.removeAttribute('aria-current');
      });
      setActive('home');

      document.addEventListener('click', e => {
        const link = e.target.closest('a[href^="#"]');
        if (!link) return;
        const target = $(link.getAttribute('href'));
        if (!target) return;
        e.preventDefault();
        const headerHeight = $('#header')?.offsetHeight || 78;
        window.scrollTo({ top: Math.max(0, target.offsetTop - headerHeight - 10), behavior: 'smooth' });
        $('#navLinks')?.classList.remove('open');
      });

      const update = () => {
        if (window.scrollY < 80) return setActive('home');
        const marker = window.scrollY + ($('#header')?.offsetHeight || 78) + window.innerHeight * .22;
        let current = 'home';
        links.forEach(link => {
          const section = $(link.getAttribute('href'));
          if (section && section.offsetTop <= marker) current = section.id;
        });
        setActive(current);
      };
      window.addEventListener('scroll', update, { passive: true });
      update();
    }

    function esc(value) {
      return String(value ?? '').replace(/[&<>'"]/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', "'":'&#39;', '"':'&quot;' }[c]));
    }
  });
})();
