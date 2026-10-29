const {chromium}=require('playwright');
const {spawn}=require('node:child_process');
const {mkdirSync,writeFileSync}=require('node:fs');
const path=require('node:path');
const assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..'), out=path.join(root,'qa-artifacts/real-estate-landing/v1.4');
const url='http://127.0.0.1:51924';
mkdirSync(out,{recursive:true});
const app=spawn('dotnet',[path.join(root,'TodoX.Landing/bin/Release/net10.0/TodoX.Landing.dll'),'--urls',url],{cwd:path.join(root,'TodoX.Landing'),stdio:'ignore',windowsHide:true,env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ConnectionStrings__TodoXSaaS:'Host=127.0.0.1;Port=1;Database=disabled;Username=qa;Password=qa;Timeout=1',SharedMedia__StorageRoot:path.join(root,'qa-artifacts/real-estate-landing/v1.2/isolated-media')}});
async function main(){let browser;const results=[];try{
  for(let i=0;i<40;i++){try{if((await fetch(url+'/health')).ok)break}catch{}await new Promise(r=>setTimeout(r,400))}
  browser=await chromium.launch({executablePath:'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',headless:true});
  for(const [width,height] of [[1440,900],[768,1024],[390,844],[375,812]]){
    const page=await browser.newPage({viewport:{width,height},reducedMotion:'reduce'}),errors=[];
    page.on('pageerror',e=>errors.push(e.message));
    await page.route('**/api/landing/intro-video',r=>r.fulfill({json:{enabled:true,videoUrl:'/media/landing/intro/videos/11111111111111111111111111111111.mp4',posterUrl:null,playbackMode:'CLICK_TO_PLAY',mutedByDefault:true,showControls:true}}));
    const response=await page.goto(url+'/',{waitUntil:'networkidle'});assert.equal(response.status(),200);
    await page.waitForFunction(()=>document.querySelector('.phone-screen').classList.contains('intro-ready'));
    const link=page.locator('.industry-pill-real-estate');assert.equal(await link.getAttribute('href'),'/bat-dong-san');
    assert.equal(await page.locator('.industry-pills .industry-pill').count(),7);
    assert.equal(await page.locator('.phone-content').count(),0);
    const layout=await page.evaluate(()=>{const rect=s=>{const r=document.querySelector(s).getBoundingClientRect();return {x:r.x,y:r.y,right:r.right,bottom:r.bottom,width:r.width,height:r.height}};return {phone:rect('.phone'),pills:rect('.industry-pills'),play:rect('.phone-screen .play'),overflow:document.documentElement.scrollWidth>innerWidth}});
    assert.equal(layout.overflow,false);assert(layout.pills.x>=layout.phone.right-2 || layout.pills.y>=layout.phone.bottom-2,JSON.stringify(layout));
    await page.screenshot({path:path.join(out,`home-${width}.png`),fullPage:true});
    await page.locator('.hero').screenshot({path:path.join(out,`hero-${width}.png`)});
    await page.locator('.phone-screen .play').click();
    await page.waitForFunction(()=>document.querySelector('.intro-video').currentTime>.15,{timeout:12000});
    const playback=await page.locator('.intro-video').evaluate(v=>({time:v.currentTime,paused:v.paused,muted:v.muted}));
    assert.equal(playback.paused,false);await page.locator('.intro-sound').click();assert.equal(await page.locator('.intro-video').evaluate(v=>v.muted),false);
    await page.locator('.intro-video').click();assert.equal(await page.locator('.intro-video').evaluate(v=>v.paused),true);
    await link.focus();await page.keyboard.press('Enter');await page.waitForURL('**/bat-dong-san');
    assert.equal((await page.request.get(url+'/bat-dong-san')).status(),200);
    assert.equal(await page.getByText('Quy trình triển khai',{exact:true}).count(),0);
    assert.equal(await page.locator('.service-card').count(),5);
    assert.equal(await page.locator('#leadForm').count(),1);
    assert.equal(await page.locator('footer').count(),1);
    const realOverflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth);assert.equal(realOverflow,false);
    await page.screenshot({path:path.join(out,`real-estate-${width}.png`),fullPage:true});
    assert.deepEqual(errors,[]);results.push({viewport:`${width}x${height}`,layout,playback,realOverflow,errors});await page.close();
  }
  writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results.map(r=>({viewport:r.viewport,layout:r.layout,playback:r.playback})),null,2));
}finally{if(browser)await browser.close();app.kill()}}
main().catch(e=>{console.error(e);process.exitCode=1});