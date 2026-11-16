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

    // Eased glide (ease-in-out cubic). Snapping is paused while gliding: native smooth scroll + scroll-snap fight each
    // other and stutter. A new click continues from wherever the current glide is.
    function glide(t, to, done) {
        var from = t.scrollLeft;
        var distance = to - from;
        if (Math.abs(distance) < 1) { done(); return; }
        var duration = Math.min(700, 380 + Math.abs(distance) * 0.25);
        var token = (t.__todoxGlide || 0) + 1;
        t.__todoxGlide = token;
        t.classList.add('is-gliding');
        var start = null;
        function ease(x) { return x < .5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2; }
        function finish() {
            if (t.__todoxGlide !== token) return;
            t.scrollLeft = to;
            t.classList.remove('is-gliding');
            done();
        }
        function frame(now) {
            if (t.__todoxGlide !== token) return;
            if (start === null) start = now;
            var p = Math.min(1, (now - start) / duration);
            t.scrollLeft = from + distance * ease(p);
            if (p < 1) requestAnimationFrame(frame); else finish();
        }
        requestAnimationFrame(frame);
        // Animation frames pause in background tabs: land on the target anyway.
        setTimeout(finish, duration + 150);
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
        // Count from the glide's target when clicked mid-glide, so quick double clicks move two pages.
        var position = t.__todoxTarget != null && t.classList.contains('is-gliding') ? t.__todoxTarget : t.scrollLeft;
        var current = Math.round(position / step);
        var target = Math.max(0, Math.min(items.length - 1, current + dir * perPage));
        t.__todoxTarget = items[target].offsetLeft - origin;
        glide(t, t.__todoxTarget, function () { update(slider); });
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
