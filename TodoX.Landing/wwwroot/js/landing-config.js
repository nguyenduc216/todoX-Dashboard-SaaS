window.TODOX_LANDING_CONFIG = {
  dashboardUrl: "https://dashboard.todox.vn",
  contactEndpoint: "/api/contact-leads",
  industryEndpoint: "/api/industry-solutions",
  environment: "production"
};

(() => {
  const ensureAsset = (tag, attrs, id) => {
    if (document.getElementById(id)) return;
    const element = document.createElement(tag);
    element.id = id;
    Object.entries(attrs).forEach(([key, value]) => element.setAttribute(key, value));
    document.head.appendChild(element);
  };

  window.TODOX_LANDING_ASSET_VERSION = "20260810-4";

  // Industry cards and the video popup are now owned by js/landing.js + css/landing.css.
  // Do not inject the legacy landing-industries/patch/fix bundle here; it creates
  // competing modal markup and player lifecycles.

  const applyFounderPhoto = () => {
    const founder = document.querySelector('.founder-image');
    if (!founder) return;

    founder.style.backgroundImage =
      "linear-gradient(to top, rgba(0,0,0,.32), transparent 55%), url('/img/landing/tran-trong-tuyen.png?v=20260809-3')";
    founder.style.backgroundPosition = 'center top';
    founder.style.backgroundSize = 'contain';
    founder.style.backgroundRepeat = 'no-repeat';
    founder.style.backgroundColor = '#0b0d10';
    founder.setAttribute('role', 'img');
    founder.setAttribute('aria-label', 'Trần Trọng Tuyên - Founder TodoX');

    if (!document.getElementById('founder-photo-style')) {
      const style = document.createElement('style');
      style.id = 'founder-photo-style';
      style.textContent = '.founder-image::after{display:none!important;content:none!important;}';
      document.head.appendChild(style);
    }
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', applyFounderPhoto, { once: true });
  } else {
    applyFounderPhoto();
  }
})();
