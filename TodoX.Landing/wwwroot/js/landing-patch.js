(() => {
  const apply = () => {
    const footerDashboard = document.querySelector('footer [data-dashboard-link]');
    if (footerDashboard) {
      footerDashboard.innerHTML = '<i class="fa-solid fa-right-to-bracket"></i> Đăng nhập Dashboard';
    }

    const phoneScreen = document.querySelector('.phone-screen');
    if (phoneScreen) {
      phoneScreen.style.backgroundImage =
        "linear-gradient(to top, rgba(0,0,0,.86), rgba(0,0,0,.12) 52%), url('/img/landing/chatstaff-consultant.jpg')";
      phoneScreen.style.backgroundPosition = 'center';
      phoneScreen.style.backgroundSize = 'cover';
    }
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', apply, { once: true });
  else apply();
})();
