window.TODOX_LANDING_CONFIG = {
  dashboardUrl: "https://dashboard.todox.vn",
  contactEndpoint: "/api/contact-leads",
  environment: "production"
};

(() => {
  const loadFounderPhoto = () => {
    if (document.querySelector('script[data-founder-photo]')) return;

    const script = document.createElement('script');
    script.src = '/js/founder-photo.js?v=20260809-2';
    script.async = false;
    script.dataset.founderPhoto = 'true';
    script.onerror = () => console.error('TodoX: founder-photo.js could not be loaded.');
    document.body.appendChild(script);
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', loadFounderPhoto, { once: true });
  } else {
    loadFounderPhoto();
  }
})();
