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

  window.TODOX_LANDING_ASSET_VERSION = "20260811-6";

  ensureAsset("link", {
    rel: "stylesheet",
    href: "/css/landing-industry-ux.css?v=20260810-2"
  }, "todox-landing-industry-ux-css");

  ensureAsset("script", {
    src: "/js/landing-industry-ux.js?v=20260810-2",
    defer: "defer"
  }, "todox-landing-industry-ux-js");

  ensureAsset("link", {
    rel: "stylesheet",
    href: "/css/landing-industry-multivideo.css?v=20260810-2"
  }, "todox-landing-industry-multivideo-css");

  ensureAsset("script", {
    src: "/js/landing-industry-multivideo.js?v=20260810-2",
    defer: "defer"
  }, "todox-landing-industry-multivideo-js");

  ensureAsset("link", {
    rel: "stylesheet",
    href: "/css/landing-brand.css?v=20260810-1"
  }, "todox-landing-brand-css");

  ensureAsset("link", {
    rel: "stylesheet",
    href: "/css/landing-navigation-fix.css?v=20260810-1"
  }, "todox-landing-navigation-fix-css");

  ensureAsset("script", {
    src: "/js/landing-navigation-fix.js?v=20260810-1",
    defer: "defer"
  }, "todox-landing-navigation-fix-js");

  ensureAsset("link", {
    rel: "stylesheet",
    href: "/css/landing-mobile-compact.css?v=20260811-6"
  }, "todox-landing-mobile-compact-css");

  ensureAsset("script", {
    src: "/js/landing-mobile-swiper-fix.js?v=20260811-6",
    defer: "defer"
  }, "todox-landing-mobile-swiper-fix-js");

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
