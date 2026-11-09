// Keeps TodoX videos inside TodoX: the browser's own "Download"/"Save video as" are hidden (the server refuses
// those requests anyway), and the signed-in media cookie is issued so <video> elements can play.
(function () {
  "use strict";

  function harden(video) {
    if (!video || video.dataset.todoxGuard === "1") return;
    video.dataset.todoxGuard = "1";
    video.setAttribute("controlsList", "nodownload");
    video.setAttribute("disableRemotePlayback", "");
  }

  function hardenAll(root) {
    if (!root || !root.querySelectorAll) return;
    if (root.tagName === "VIDEO") harden(root);
    root.querySelectorAll("video").forEach(harden);
  }

  function reloadFailedVideos() {
    document.querySelectorAll("video").forEach(function (v) {
      if (v.error || v.networkState === 3 /* NETWORK_NO_SOURCE */) {
        try { v.load(); } catch (e) { /* ignore */ }
      }
    });
  }

  // A video that failed before the media cookie existed is retried once the session is ready.
  let resolveReady;
  const sessionReady = new Promise(function (r) { resolveReady = r; });
  document.addEventListener("error", function (e) {
    const el = e.target;
    if (!el || (el.tagName !== "VIDEO" && el.tagName !== "SOURCE")) return;
    const video = el.tagName === "VIDEO" ? el : el.parentElement;
    if (!video || video.dataset.todoxRetried === "1") return;
    video.dataset.todoxRetried = "1";
    sessionReady.then(function () { try { video.load(); } catch (err) { /* ignore */ } });
  }, true);

  document.addEventListener("contextmenu", function (e) {
    if (e.target && e.target.closest && e.target.closest("video")) e.preventDefault();
  }, true);

  function start() {
    hardenAll(document);
    new MutationObserver(function (records) {
      records.forEach(function (r) { r.addedNodes.forEach(function (n) { if (n.nodeType === 1) hardenAll(n); }); });
    }).observe(document.documentElement, { childList: true, subtree: true });
  }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start); else start();

  window.todoxMedia = {
    ensureSession: async function (ticket) {
      try {
        const response = await fetch("/api/media/session", {
          method: "POST",
          credentials: "same-origin",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ ticket: ticket })
        });
        if (response.ok) { resolveReady(); reloadFailedVideos(); }
        return response.ok;
      } catch (e) {
        return false;
      }
    },
    clearSession: async function () {
      try { await fetch("/api/media/session/clear", { method: "POST", credentials: "same-origin" }); } catch (e) { /* ignore */ }
    }
  };
})();
