// Horizontal card slider ([data-todox-slider]): arrows scroll one page, and are disabled at either end.
// Works by event delegation, so sliders rendered later by Blazor need no setup.
(function () {
    function track(slider) { return slider && slider.querySelector('[data-todox-slider-track]'); }

    function update(slider) {
        var t = track(slider);
        if (!t) return;
        var max = t.scrollWidth - t.clientWidth - 2;
        var prev = slider.querySelector('[data-todox-slide="-1"]');
        var next = slider.querySelector('[data-todox-slide="1"]');
        if (prev) prev.disabled = t.scrollLeft <= 2;
        if (next) next.disabled = t.scrollLeft >= max;
        slider.classList.toggle('is-static', max <= 0);
    }

    document.addEventListener('click', function (e) {
        var button = e.target.closest && e.target.closest('[data-todox-slide]');
        if (!button) return;
        var slider = button.closest('[data-todox-slider]');
        var t = track(slider);
        if (!t) return;
        var dir = parseInt(button.getAttribute('data-todox-slide'), 10) || 1;
        var items = t.querySelectorAll('.todox-slider-item');
        if (!items.length) return;
        // Jump a whole page of cards, landing exactly on a card (scroll-snap would otherwise stop at the next one).
        var origin = items[0].offsetLeft;
        var step = items.length > 1 ? items[1].offsetLeft - origin : t.clientWidth;
        var perPage = Math.max(1, Math.round(t.clientWidth / step));
        var current = Math.round(t.scrollLeft / step);
        var target = Math.max(0, Math.min(items.length - 1, current + dir * perPage));
        t.scrollTo({ left: items[target].offsetLeft - origin, behavior: 'smooth' });
        // Scroll events can be throttled (background tab); settle the arrows once the scroll ends anyway.
        setTimeout(function () { update(slider); }, 700);
    });

    document.addEventListener('scroll', function (e) {
        var t = e.target;
        if (t && t.matches && t.matches('[data-todox-slider-track]')) update(t.closest('[data-todox-slider]'));
    }, true);

    var pending = false;
    function refreshAll() {
        if (pending) return;
        pending = true;
        setTimeout(function () {
            pending = false;
            document.querySelectorAll('[data-todox-slider]').forEach(update);
        }, 60);
    }
    window.addEventListener('resize', refreshAll);
    new MutationObserver(refreshAll).observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('DOMContentLoaded', refreshAll);
})();
