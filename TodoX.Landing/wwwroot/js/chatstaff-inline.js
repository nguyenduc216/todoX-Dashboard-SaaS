// Homepage ChatStaff box (#chatstaff): a real chat that rides on the ChatStaff widget loaded in <head>.
// The widget owns the session, socket and conversation. This box only mirrors its messages and sends through
// its composer, so the corner bubble and this box show one and the same conversation.
// Messages are rendered with textContent only.
(() => {
  'use strict';

  const box = document.getElementById('messages');
  const input = document.getElementById('chatInput');
  const send = document.getElementById('sendChat');
  const status = document.getElementById('chatStatus');
  if (!box || !input || !send) return;

  const WAIT_MS = 20000;
  const defaultWelcome = (box.querySelector('.msg.bot') || {}).textContent || 'Xin chào! TodoX có thể hỗ trợ gì cho anh/chị?';
  let shadow = null;
  let lastSignature = '';
  let renderQueued = false;

  const setStatus = (text, state) => {
    if (!status) return;
    status.textContent = '● ' + text;
    status.dataset.state = state;
  };

  const widgetHost = () => document.querySelector('[data-chatstaff-widget]');

  const openWidget = () => {
    const launcher = shadow && shadow.getElementById('launcher');
    if (launcher && !launcher.classList.contains('open')) launcher.click();
  };

  const rowText = (row) => {
    const bubble = row.querySelector('.msg-bubble');
    if (!bubble) return '';
    // Message text is the bubble's own text nodes; attachments are child elements.
    const text = Array.from(bubble.childNodes)
      .filter((node) => node.nodeType === Node.TEXT_NODE)
      .map((node) => node.textContent)
      .join('')
      .trim();
    if (text && !/^\[[a-z_]+\]$/.test(text)) return text;
    if (bubble.querySelector('.att-img')) return '[Hình ảnh]';
    if (bubble.querySelector('.att-video')) return '[Video]';
    if (bubble.querySelector('.att')) return '[Tệp đính kèm]';
    return '';
  };

  const readRows = () => {
    const source = shadow && shadow.getElementById('messages');
    if (!source) return [];
    const rows = [];
    source.querySelectorAll('.msg-row').forEach((row) => {
      if (row.classList.contains('typing')) { rows.push({ kind: 'typing', text: '' }); return; }
      const text = rowText(row);
      if (!text) return;
      const kind = row.classList.contains('customer') ? 'user' : row.classList.contains('system') ? 'system' : 'bot';
      rows.push({ kind, text });
    });
    return rows;
  };

  const bubble = (kind, text) => {
    const el = document.createElement('div');
    el.className = 'msg ' + (kind === 'user' ? 'user' : 'bot') + (kind === 'system' ? ' cs-system' : '');
    if (kind === 'typing') {
      el.classList.add('cs-typing');
      el.setAttribute('aria-label', 'Đang trả lời');
      for (let i = 0; i < 3; i += 1) el.appendChild(document.createElement('span'));
    } else {
      el.textContent = text;
    }
    return el;
  };

  const render = () => {
    renderQueued = false;
    // The widget shows an .empty-error block when ChatStaff refuses the page (e.g. domain not allowed).
    const failed = shadow && shadow.querySelector('#messages .empty-error');
    if (failed) setStatus('Chưa kết nối được ChatStaff', 'error');
    else setStatus('Đang hoạt động', 'online');
    const rows = readRows();
    if (!rows.some((row) => row.kind === 'bot' || row.kind === 'system')) rows.unshift({ kind: 'bot', text: defaultWelcome });
    const signature = JSON.stringify(rows);
    if (signature === lastSignature) return;
    lastSignature = signature;
    const atBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 40;
    box.replaceChildren(...rows.map((row) => bubble(row.kind, row.text)));
    if (atBottom || rows.length && rows[rows.length - 1].kind !== 'bot') box.scrollTop = box.scrollHeight;
  };

  const queueRender = () => {
    if (renderQueued) return;
    renderQueued = true;
    // setTimeout, not requestAnimationFrame: rAF is paused in background tabs and replies would not show up.
    setTimeout(render, 30);
  };

  const sendMessage = () => {
    const value = input.value.trim();
    if (!value) return;
    const widgetInput = shadow && shadow.getElementById('input');
    const form = shadow && shadow.getElementById('form');
    if (!widgetInput || !form || shadow.querySelector('#messages .empty-error')) {
      setStatus('Chưa kết nối được, vui lòng thử lại', 'error');
      return;
    }
    widgetInput.value = value;
    widgetInput.dispatchEvent(new Event('input', { bubbles: true }));
    if (typeof form.requestSubmit === 'function') form.requestSubmit();
    else shadow.getElementById('send').click();
    input.value = '';
    input.focus();
  };

  const attach = () => {
    const host = widgetHost();
    const root = host && host.shadowRoot;
    const source = root && root.getElementById('messages');
    if (!source || !root.getElementById('form')) return false;
    shadow = root;
    new MutationObserver(queueRender).observe(source, { childList: true, subtree: true, characterData: true });
    render();
    return true;
  };

  send.addEventListener('click', sendMessage);
  input.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && !event.isComposing) {
      event.preventDefault();
      sendMessage();
    }
  });

  // "Chuyển nhân viên" in the lead card opens the full chat panel.
  const handover = document.querySelector('#chatstaff .lead-card .btn');
  if (handover) handover.addEventListener('click', () => { if (shadow) openWidget(); });

  if (attach()) return;
  const started = Date.now();
  const timer = setInterval(() => {
    if (attach()) { clearInterval(timer); return; }
    if (Date.now() - started > WAIT_MS) {
      clearInterval(timer);
      setStatus('Chưa kết nối được ChatStaff', 'error');
    }
  }, 300);
})();
