const fs = require('fs');
const assert = require('node:assert/strict');
const source = fs.readFileSync('TodoX.Web/wwwroot/js/rvideo-view-state.js','utf8').replaceAll('export function','function');
const store = new Map();
const el = { dataset:{rvideoScroll:'preview'},clientHeight:200,scrollHeight:1000,scrollTop:0 };
let handler;
const root = { querySelectorAll:()=>[el],addEventListener:(n,h)=>handler=h,removeEventListener:()=>{} };
const api = new Function('sessionStorage','document',source+';return {open,attach,saveTab,detach};')(
 {getItem:k=>store.get(k),setItem:(k,v)=>store.set(k,v)}, {getElementById:()=>root});
assert.equal(api.open('project7',0),0);api.attach('project7',0);
el.scrollTop=450;handler({target:el});api.saveTab('project7',1);api.detach();
assert.equal(api.open('project7',0),1);
assert.equal(api.open('project8',0),0);
api.open('project7',0);el.scrollHeight=500;api.attach('project7',1);assert.equal(el.scrollTop,300); // clamp saved 450 to resized content
el.scrollTop=600;api.attach('project7',1);assert.equal(el.scrollTop,600); // poll never forces old position
console.log('session tab isolation, scroll capture and non-disruptive attach PASS');
