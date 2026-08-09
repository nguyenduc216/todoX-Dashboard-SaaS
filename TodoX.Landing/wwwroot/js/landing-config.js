window.TODOX_LANDING_CONFIG = {
  dashboardUrl: "https://dashboard.todox.vn",
  contactEndpoint: "/api/contact-leads",
  environment: "production"
};

(() => {
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
