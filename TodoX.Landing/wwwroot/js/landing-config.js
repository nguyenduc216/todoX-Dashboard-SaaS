window.TODOX_LANDING_CONFIG = {
  dashboardUrl: "https://dashboard.todox.vn",
  contactEndpoint: "/api/contact-leads",
  environment: "production"
};

(() => {
  const script = document.createElement("script");
  script.src = "js/founder-photo.js";
  script.defer = true;
  document.head.appendChild(script);
})();
