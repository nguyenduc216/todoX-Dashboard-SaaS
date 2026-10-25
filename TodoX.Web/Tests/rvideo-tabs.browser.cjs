// Local MudBlazor 9.6 component harness; requires Playwright and RVIDEO_UI_HARNESS_URL.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE});
 const page=await browser.newPage();
 await page.goto(process.env.RVIDEO_UI_HARNESS_URL || 'http://127.0.0.1:5198');
 await page.getByRole('tab',{name:'Info',exact:true}).waitFor();await page.waitForTimeout(2500);
 async function select(name,text){
  await page.getByRole('tab',{name,exact:true}).click();
  await page.waitForFunction(name=>document.querySelectorAll('[role=tab]') && [...document.querySelectorAll('[role=tab]')].some(e=>e.textContent===name && e.getAttribute('aria-selected')==='true'),name);
  assert(await page.getByRole('tab',{name,exact:true}).getAttribute('aria-selected')==='true');
  const visible=await page.locator('.mud-tabs-panels > .mud-tab-panel').evaluateAll(els=>els.filter(e=>getComputedStyle(e).display!=='none').map(e=>e.textContent));
  assert.equal(visible.length,1);assert(visible[0].includes(text));
 }
 await select('Info','Info content');await select('Preview','Preview content');await select('Result','Result content');await select('Info','Info content');
 await select('Preview','Preview content');
 await page.locator('[data-rvideo-scroll="preview"]').evaluate(el=>{el.scrollTop=700;el.dispatchEvent(new Event('scroll',{bubbles:true}));});
 await page.reload();await page.getByRole('tab',{name:'Preview',exact:true}).waitFor();await page.waitForTimeout(2500);
 assert.equal(await page.getByRole('tab',{name:'Preview',exact:true}).getAttribute('aria-selected'),'true');
 assert(Math.abs(await page.locator('[data-rvideo-scroll="preview"]').evaluate(el=>el.scrollTop)-700)<10);
 await select('Result','Result content');
 const handle=await page.locator('#player').elementHandle();
 await page.reload();await page.getByRole('tab',{name:'Result',exact:true}).waitFor();await page.waitForTimeout(2500);
 assert.equal(await page.getByRole('tab',{name:'Result',exact:true}).getAttribute('aria-selected'),'true');
 console.log('Chromium MudBlazor 9.6: tab highlight/content 0-1-2-0, single visible panel, F5 tab and 700px preview scroll PASS');
 await browser.close();
})().catch(e=>{console.error(e);process.exitCode=1;});
