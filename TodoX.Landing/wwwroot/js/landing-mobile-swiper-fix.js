(() => {
  const mobileQuery = window.matchMedia('(max-width: 768px)');

  function tuneIndustrySwiper() {
    const el = document.querySelector('.industrySwiper');
    if (!el || !mobileQuery.matches) return;

    // Swiper stores its live instance on the root element.
    const swiper = el.swiper;
    if (!swiper) return;

    try {
      swiper.params.slidesPerView = 'auto';
      swiper.params.centeredSlides = true;
      swiper.params.centeredSlidesBounds = true;
      swiper.params.spaceBetween = 12;
      swiper.params.loop = false;
      swiper.params.rewind = true;
      swiper.params.speed = 420;
      swiper.params.threshold = 10;
      swiper.params.resistance = true;
      swiper.params.resistanceRatio = 0.35;
      swiper.params.touchRatio = 1;
      swiper.params.longSwipes = true;
      swiper.params.longSwipesRatio = 0.2;
      swiper.params.longSwipesMs = 280;
      swiper.params.followFinger = true;
      swiper.params.allowTouchMove = true;
      swiper.params.grabCursor = true;
      swiper.params.touchReleaseOnEdges = true;
      swiper.params.watchOverflow = true;
      swiper.params.watchSlidesProgress = true;
      swiper.params.observer = true;
      swiper.params.observeParents = true;

      if (swiper.navigation) {
        try { swiper.navigation.enable(); } catch {}
      }

      swiper.update();
    } catch (error) {
      console.debug('TodoX mobile Swiper tuning skipped', error);
    }
  }

  function scheduleTune() {
    let attempt = 0;
    const timer = window.setInterval(() => {
      attempt += 1;
      tuneIndustrySwiper();
      const instance = document.querySelector('.industrySwiper')?.swiper;
      if (instance || attempt >= 20) window.clearInterval(timer);
    }, 120);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', scheduleTune, { once: true });
  } else {
    scheduleTune();
  }

  if (mobileQuery.addEventListener) {
    mobileQuery.addEventListener('change', event => {
      if (event.matches) scheduleTune();
    });
  }
})();
