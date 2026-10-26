// V14-4: at Extra large text (and from 115 %) the phone app bar, tab bar and wizard steps wrap / use short labels instead of "Room boa…", "Appro…", "Dates & pa…".
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['src/app/mobile/tabs.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/tabs-large.mjs',external:['react','lucide-react'],logLevel:'error'});
const T=await import('./.generated/tabs-large.mjs');
const css=readFileSync('src/styles/z-largetext.css','utf8').replace(/\/\*[\s\S]*?\*\//g,'');
const rule=sel=>{const i=css.indexOf(sel+'{');if(i<0)return '';return css.slice(i,css.indexOf('}',i)+1);};
export function register({test,assert}){
  test('[LARGETEXT] large-text switch starts at the 115 % step',()=>{
    assert.equal(T.isLargeText(0.9),false);assert.equal(T.isLargeText(1),false);assert.equal(T.isLargeText(1.15),true);assert.equal(T.isLargeText(1.3),true);
  });
  test('[LARGETEXT] tab labels: short label only at large text and only when the tab has one; every short label is shorter',()=>{
    const all=Object.values(T.roleTabs).flat();
    const approvals=all.find(t=>t.key==='approvals');
    assert.equal(T.tabLabel(approvals,false),'Approvals');assert.equal(T.tabLabel(approvals,true),'Approve');
    const home=all.find(t=>t.key==='home');assert.equal(T.tabLabel(home,true),'Home');
    for(const t of all)if(t.short)assert.ok(t.short.length<t.label.length,t.key);
  });
  test('[LARGETEXT] tab labels wrap to at most two lines (no ellipsis) and cap their own growth; tab bar and app bar grow with --hcc-text',()=>{
    const r=rule('.m-tabbar button>span:last-child');
    assert.ok(r.length>0);assert.ok(!r.includes('text-overflow:ellipsis'));assert.ok(r.includes('white-space:normal'));assert.ok(r.includes('-webkit-line-clamp:2'));
    assert.ok(r.includes('min(var(--hcc-text,1), 1.1)'));
    const grow='max(64px,calc(64px + (var(--hcc-text,1) - 1) * 60px))';
    assert.ok(css.includes(':root{--tabbar:'+grow+'}'));assert.ok(css.includes('.m-shell{--tabbar:'+grow+';--appbar:max(56px,calc(56px + (var(--hcc-text,1) - 1) * 60px))}'));
  });
  test('[LARGETEXT] at large text the app-bar title wraps to two lines and the business-date line is dropped; wizard step names wrap',()=>{
    const t=rule('.m-shell.m-large .m-title');assert.ok(t.includes('white-space:normal'));assert.ok(t.includes('-webkit-line-clamp:2'));
    assert.ok(rule('.m-shell.m-large .m-date').includes('display:none'));
    const e=rule('.m-steps em');assert.ok(e.includes('white-space:normal'));assert.ok(!e.includes('ellipsis'));
  });
  test('[LARGETEXT] MobileShell puts m-large on the shell from the saved text size and renders the tab label through tabLabel',()=>{
    const s=readFileSync('src/app/mobile/MobileShell.tsx','utf8');
    assert.ok(s.includes('isLargeText(usePrefs().prefs.textScale)'));assert.ok(s.includes("(large?' m-large':'')"));assert.ok(s.includes('tabLabel(t,large)'));
  });
  test('[LARGETEXT] z-largetext.css has no colour literals (tokens only)',()=>{
    assert.doesNotMatch(css,/#[0-9a-fA-F]{3,8}\b/);
  });
}
