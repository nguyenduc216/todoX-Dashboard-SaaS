let cachedConfig = null;

async function loadConfig() {
  if (cachedConfig) return cachedConfig;

  const url = chrome.runtime.getURL("config.json");
  const res = await fetch(url);
  if (!res.ok) {
    throw new Error("Cannot load TodoX extension config.json");
  }

  cachedConfig = await res.json();
  return cachedConfig;
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || message.type !== "TODOX_ADD_REFERENCE_VIDEO") {
    return false;
  }

  (async () => {
    try {
      const config = await loadConfig();
      const apiUrl = `${config.apiBaseUrl.replace(/\/$/, "")}/api/extension/reference-videos`;

      const res = await fetch(apiUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Authorization": `Bearer ${config.extensionToken}`
        },
        body: JSON.stringify(message.payload)
      });

      const data = await res.json().catch(() => ({}));

      if (!res.ok) {
        sendResponse({
          success: false,
          message: data.message || `TodoX API error ${res.status}`
        });
        return;
      }

      sendResponse({
        success: true,
        message: data.message || "Da them link vao TodoX",
        data
      });
    } catch (err) {
      sendResponse({
        success: false,
        message: err && err.message ? err.message : "Unknown extension error"
      });
    }
  })();

  return true;
});
