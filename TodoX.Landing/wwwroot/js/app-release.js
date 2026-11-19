// "Tải app": shows the version and size of the latest build next to each download button.
// <span data-app-release="reupvideo"></span> → "Phiên bản 1.0.0 · 68 MB". Keeps the built-in text when unavailable.
(function () {
  var targets = document.querySelectorAll('[data-app-release]');
  var apps = {};
  targets.forEach(function (el) { apps[el.getAttribute('data-app-release')] = true; });
  Object.keys(apps).forEach(function (app) {
    fetch('/api/landing/apps/' + encodeURIComponent(app), { headers: { Accept: 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (release) {
        if (!release || !release.version) { return; }
        var mb = release.size ? ' · ' + Math.max(1, Math.round(release.size / 1048576)) + ' MB' : '';
        document.querySelectorAll('[data-app-release="' + app + '"]').forEach(function (el) {
          el.textContent = 'Phiên bản ' + release.version + mb;
        });
      })
      .catch(function () { /* keep the built-in text */ });
  });
})();
