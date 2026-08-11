(() => {
  function init() {
    const nav = document.getElementById('navLinks');
    if (!nav) return;

    const industries = document.getElementById('industries');
    const workflowShell = document.getElementById('aiWorkflow');
    const workflowSection = workflowShell?.closest('section');
    const founder = document.getElementById('founder');

    // The original markup reused #solutions for the workflow section.
    // Rename that section and create a real, dedicated Solutions section.
    if (workflowSection?.id === 'solutions') {
      workflowSection.id = 'workflow-section';
    }

    let solutions = document.getElementById('solutions');
    if (!solutions && workflowSection) {
      solutions = document.createElement('section');
      solutions.className = 'section todox-solutions-section';
      solutions.id = 'solutions';
      solutions.innerHTML = `
        <div class="container">
          <div class="section-title">
            <span class="eyebrow"><i class="fa-solid fa-sparkles"></i> GIẢI PHÁP TODOX</span>
            <h2>Từ ý tưởng đến hệ thống nội dung <span class="gold-text">tăng trưởng tự động</span></h2>
            <p>TodoX kết hợp AI Video, tự động hóa nội dung và dữ liệu khách hàng để doanh nghiệp xây kênh, làm affiliate và triển khai chiến dịch đa nền tảng.</p>
          </div>
          <div class="todox-solutions-grid">
            <article class="todox-solution-card">
              <div class="todox-solution-icon"><i class="fa-solid fa-clapperboard"></i></div>
              <span class="todox-solution-no">01</span>
              <h3>AI Video Automation</h3>
              <p>Tạo hình ảnh, kịch bản, giọng đọc và video bán hàng theo một workflow thống nhất.</p>
            </article>
            <article class="todox-solution-card">
              <div class="todox-solution-icon"><i class="fa-solid fa-chart-line"></i></div>
              <span class="todox-solution-no">02</span>
              <h3>Xây kênh & Affiliate</h3>
              <p>Sản xuất video ngắn theo ngành hàng, tối ưu cho TikTok, Reels, Shorts và social commerce.</p>
            </article>
            <article class="todox-solution-card">
              <div class="todox-solution-icon"><i class="fa-solid fa-layer-group"></i></div>
              <span class="todox-solution-no">03</span>
              <h3>Nội dung đa nền tảng</h3>
              <p>Một nguồn dữ liệu có thể phát triển thành nhiều định dạng nội dung và nhiều kênh phân phối.</p>
            </article>
            <article class="todox-solution-card">
              <div class="todox-solution-icon"><i class="fa-solid fa-gears"></i></div>
              <span class="todox-solution-no">04</span>
              <h3>Quy trình có thể nhân rộng</h3>
              <p>Chuẩn hóa đầu vào, kiểm duyệt, sản xuất và bàn giao để doanh nghiệp dễ mở rộng quy mô.</p>
            </article>
          </div>
        </div>`;
    }

    // Enforce the same order as the header navigation:
    // Home -> About -> Industries -> Solutions -> Workflow -> ChatStaff.
    if (solutions && workflowSection) {
      const anchor = founder || document.getElementById('about');
      if (industries && anchor?.parentNode) {
        anchor.parentNode.insertBefore(industries, anchor.nextSibling);
      }
      if (industries?.parentNode) {
        industries.parentNode.insertBefore(solutions, industries.nextSibling);
      }
      if (solutions.parentNode) {
        solutions.parentNode.insertBefore(workflowSection, solutions.nextSibling);
      }
    }

    const links = Array.from(nav.querySelectorAll('a[href^="#"]'));

    function resolveTarget(link) {
      const selector = link.getAttribute('href');
      const target = selector ? document.querySelector(selector) : null;
      if (!target) return null;
      // #aiWorkflow is an inner shell. For navigation calculations use its section.
      return target.closest('section') || target;
    }

    function absoluteTop(el) {
      return el.getBoundingClientRect().top + window.scrollY;
    }

    function syncActive() {
      const header = document.getElementById('header');
      const probe = window.scrollY + (header?.offsetHeight || 78) + Math.min(window.innerHeight * 0.22, 180);
      const targets = links
        .map(link => ({ link, target: resolveTarget(link) }))
        .filter(x => x.target)
        .sort((a, b) => absoluteTop(a.target) - absoluteTop(b.target));

      let active = targets[0]?.link || null;
      for (const item of targets) {
        if (absoluteTop(item.target) <= probe) active = item.link;
        else break;
      }

      // Close to page bottom, ensure the last visible nav section wins.
      if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 8) {
        active = targets[targets.length - 1]?.link || active;
      }

      links.forEach(link => link.classList.toggle('active', link === active));
    }

    let ticking = false;
    function scheduleSync() {
      if (ticking) return;
      ticking = true;
      requestAnimationFrame(() => {
        ticking = false;
        syncActive();
      });
    }

    window.addEventListener('scroll', scheduleSync, { passive: true });
    window.addEventListener('resize', scheduleSync, { passive: true });
    links.forEach(link => link.addEventListener('click', () => {
      window.setTimeout(syncActive, 120);
      window.setTimeout(syncActive, 650);
    }));

    // Run after landing.js has attached its legacy scroll handler so this state wins.
    window.setTimeout(syncActive, 0);
    window.setTimeout(syncActive, 500);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => window.setTimeout(init, 0), { once: true });
  } else {
    window.setTimeout(init, 0);
  }
})();
