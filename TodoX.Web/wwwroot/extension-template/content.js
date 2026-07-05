(function () {
  const BUTTON_CLASS = "todox-video-collector-button";
  const MARK_ATTR = "data-todox-video-collector";

  function findVideoLinks() {
    return Array.from(document.querySelectorAll('a[href*="/video/"]'))
      .filter((link) => link.href && !link.hasAttribute(MARK_ATTR));
  }

  function nearestContainer(link) {
    return link.closest("article") || link.closest('[data-e2e*="feed"]') || link.parentElement || document.body;
  }

  function getVideoId(url) {
    const match = String(url).match(/\/video\/(\d+)/);
    return match ? match[1] : null;
  }

  function getAuthorFromUrl(url) {
    const match = String(url).match(/tiktok\.com\/(@[^/]+)/i);
    return match ? match[1] : null;
  }

  function textFrom(container, selectors) {
    for (const selector of selectors) {
      const el = container.querySelector(selector);
      const text = el && el.textContent ? el.textContent.trim() : "";
      if (text) return text;
    }
    return "";
  }

  function getHashtags(text) {
    const matches = String(text || "").match(/#[\p{L}\p{N}_]+/gu);
    return matches ? Array.from(new Set(matches)) : [];
  }

  function getThumbnail(container) {
    const img = container.querySelector("img[src]");
    return img ? img.src : null;
  }

  function buildPayload(link) {
    const container = nearestContainer(link);
    const sourceUrl = link.href;
    const description = textFrom(container, [
      '[data-e2e="video-desc"]',
      '[data-e2e="browse-video-desc"]',
      "h1",
      "h2"
    ]);
    const authorHandle = getAuthorFromUrl(sourceUrl) || textFrom(container, [
      '[data-e2e="video-author-uniqueid"]',
      '[data-e2e="browse-username"]'
    ]);

    return {
      platform: "tiktok",
      sourceUrl,
      channelName: authorHandle,
      channelUrl: authorHandle ? `https://www.tiktok.com/${authorHandle}` : null,
      authorHandle,
      title: description || document.title,
      description,
      hashtags: getHashtags(description),
      publishedAt: null,
      thumbnailUrl: getThumbnail(container),
      externalVideoId: getVideoId(sourceUrl),
      rawMetadata: {
        pageUrl: location.href,
        collectedAt: new Date().toISOString()
      }
    };
  }

  function setButtonState(button, state, text) {
    button.dataset.state = state;
    button.textContent = text;
  }

  function injectButton(link) {
    link.setAttribute(MARK_ATTR, "1");
    const container = nearestContainer(link);
    if (container.querySelector(`.${BUTTON_CLASS}`)) return;

    const button = document.createElement("button");
    button.type = "button";
    button.className = BUTTON_CLASS;
    button.textContent = "[+] todoX";
    button.addEventListener("click", (event) => {
      event.preventDefault();
      event.stopPropagation();
      setButtonState(button, "loading", "Dang them...");
      chrome.runtime.sendMessage(
        {
          type: "TODOX_ADD_REFERENCE_VIDEO",
          payload: buildPayload(link)
        },
        (response) => {
          if (chrome.runtime.lastError) {
            setButtonState(button, "error", "Loi TodoX");
            button.title = chrome.runtime.lastError.message || "TodoX extension error";
            return;
          }

          if (response && response.success) {
            setButtonState(button, "done", "Da them");
          } else {
            setButtonState(button, "error", "Loi TodoX");
            button.title = response && response.message ? response.message : "TodoX API error";
          }
        }
      );
    });

    container.style.position = container.style.position || "relative";
    container.appendChild(button);
  }

  function scan() {
    for (const link of findVideoLinks()) {
      injectButton(link);
    }
  }

  scan();
  const observer = new MutationObserver(() => scan());
  observer.observe(document.documentElement, { childList: true, subtree: true });
})();
