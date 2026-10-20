const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../wwwroot');
const html = fs.readFileSync(path.join(root, 'bat-dong-san.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'js/real-estate.js'), 'utf8');
assert.match(html, /<html lang="vi">/);
assert.equal((html.match(/<h1\b/g) || []).length, 1);
assert.equal((html.match(/class="service-card"/g) || []).length, 5);
assert.match(html, /name="consentAccepted" required/);
assert.match(html, /name="website"/);
assert.match(js, /fetch\('\/api\/contact-leads'/);
assert.match(js, /fetch\('\/api\/landing\/intro-video'/);
assert.match(js, /sourceUrl: window\.location\.href/);
assert.match(js, /if \(pending\) return/);
assert.match(js, /result\.leadCode/);
const sitemap = fs.readFileSync(path.join(root, 'sitemap.xml'), 'utf8');
assert.match(sitemap, /https:\/\/todox\.vn\/bat-dong-san/);
// Execute with mocked DOM/API to check attribution, double submit and failure preservation.
const handlers = {};
const elements = {};
const form = {
  elements: { need: { value: '' } },
  querySelector: () => elements.submit,
  addEventListener: (name, handler) => { handlers[name] = handler; },
  checkValidity: () => true, reset: () => { form.resetCount = (form.resetCount || 0) + 1; }
};
elements.submit = { disabled: false };
elements.leadFormStatus = { textContent: '', className: '' };
elements.leadForm = form;
elements.navigation = { classList: { toggle() {}, remove() {} }, addEventListener() {} };
elements.demoVideo = { muted: true, addEventListener() {} };
elements.demoFrame = { classList: { add() {}, remove() {} } };
elements.demoPlay = { querySelector: () => ({ textContent: '' }), setAttribute() {} };
elements.demoSound = { setAttribute() {} };
elements.demoFullscreen = {};
elements.demoHint = {};
const menu = { addEventListener() {}, getAttribute: () => 'false', setAttribute() {} };
const context = {
  document: { querySelector: () => menu, querySelectorAll: () => [], getElementById: id => elements[id], referrer: '' },
  window: { location: { href: 'https://todox.vn/bat-dong-san?utm_source=search', search: '?utm_source=search' } },
  FormData: class { get(name) { return ({ fullName: 'Tester', phone: '0900000000', need: 'Bản đồ – GeoLayer VFX', consentAccepted: 'on' })[name] || null; } },
  URLSearchParams,
  fetch: (url, options) => {
    if (url.includes('intro-video')) return Promise.resolve({ ok: true, json: async () => ({ enabled: false }) });
    context.calls.push(JSON.parse(options.body));
    return context.nextResponse;
  },
  calls: [], nextResponse: null
};
vm.runInNewContext(js, context);
const event = { preventDefault() {} };
(async () => {
  context.nextResponse = Promise.resolve({ ok: true, json: async () => ({ success: true, leadCode: 'LD-1', message: 'Đã nhận' }) });
  const first = handlers.submit(event);
  await handlers.submit(event);
  assert.equal(context.calls.length, 1);
  assert.equal(context.calls[0].industry, 'Bất động sản');
  assert.equal(context.calls[0].utmSource, 'search');
  assert.equal(context.calls[0].sourceUrl, context.window.location.href);
  await first;
  assert.equal(form.resetCount, 1);
  context.nextResponse = Promise.resolve({ ok: false, status: 429, json: async () => ({}) });
  await handlers.submit(event);
  assert.equal(form.resetCount, 1);
  assert.equal(elements.leadFormStatus.className, 'error');
  console.log('Real estate static and submission checks passed');
})().catch(error => { console.error(error); process.exitCode = 1; });