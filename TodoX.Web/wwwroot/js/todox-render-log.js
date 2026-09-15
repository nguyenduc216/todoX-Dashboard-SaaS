window.todoXRenderLog = window.todoXRenderLog || {
  scrollToBottom: function (elementId) {
    const element = document.getElementById(elementId);
    if (!element) return;
    element.scrollTop = element.scrollHeight;
  },
  copyText: async function (text) {
    try {
      await navigator.clipboard.writeText(text || '');
    } catch {
      const textarea = document.createElement('textarea');
      textarea.value = text || '';
      textarea.setAttribute('readonly', 'true');
      textarea.style.position = 'fixed';
      textarea.style.opacity = '0';
      document.body.appendChild(textarea);
      textarea.focus();
      textarea.select();
      document.execCommand('copy');
      document.body.removeChild(textarea);
    }
  }
};

window.todoXVideoPreview = window.todoXVideoPreview || {
  play: async function (video) {
    if (!video) return;
    video.currentTime = 0;
    try {
      await video.play();
    } catch {
    }
  }
};
