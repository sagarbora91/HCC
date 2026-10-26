// V14-3 · Tablet layout of the masters lists (desktop layout on a tablet in portrait). The browser proof is tools/tablet-masters-check.mjs
// (Playwright, 800x1280 and 1280x800); these tests pin the CSS rules it relies on, so a regression shows without a browser. Names prefixed [V14-3].
// The rules live in z-tablet-masters.css: m-masters.css and z-tablet.css are pinned line by line by the [CT-A]/[CT-C] colour-token tests.
import {readFileSync} from 'node:fs';
export function register({test,assert}){
  const tab=readFileSync('src/styles/z-tablet-masters.css','utf8');
  const mst=readFileSync('src/styles/m-masters.css','utf8');
  const noComments=s=>s.replace(/\/\*[\s\S]*?\*\//g,'');
  const block=noComments(tab.slice(tab.indexOf('V14-3')));

  test('[V14-3] the rows list and the form stack below 1040 px (hub detail column under ~640 px), side by side above',()=>{
    assert.match(mst,/\.mst-split\{display:grid;grid-template-columns:minmax\(220px,300px\) minmax\(0,1fr\)/,'side-by-side default kept for landscape');
    const m=/@media \(max-width: (\d+)px\) \{\s*\.mst-split \{ grid-template-columns: minmax\(0, 1fr\); \}\s*\.mst-detail \{ width: 100%; \}/.exec(block);
    assert.ok(m,'stacking media query for .mst-split in z-tablet-masters.css');
    const w=Number(m[1]);
    assert.ok(w>=1000&&w<1280,`breakpoint ${w} must stack an 800 and 1024 px tablet portrait but keep a 1280 px landscape side by side`);
    // hub detail column at the breakpoint = viewport - 2x25 content padding - 16 gap - 320 settings-group list = 654 px
    assert.ok(w-50-16-320>=640,'detail column at the breakpoint is still >= 640 px');
    assert.ok(mst.includes('@media(max-width:760px){.mst-split{grid-template-columns:minmax(0,1fr)}'),'the older 760 px stacking rule stays');
  });
  test('[V14-3] form inputs are never narrower than 240 px (or the pane when narrower); dated-money add row too',()=>{
    assert.match(block,/\.mst-detail input:not\(\[type=checkbox\]\):not\(\[type=radio\]\), \.mst-detail select, \.mst-detail textarea \{ min-width: min\(240px, 100%\); \}/);
    assert.match(block,/\.mst-detail \.mst-dated-add label \{ min-width: min\(240px, 100%\); \}/);
  });
  test('[V14-3] Rooms tools row (range add / renumber / out of order) uses 240 px inputs when stacked',()=>{
    const q=/@media \(max-width: 1040px\) \{([\s\S]*?)\n\}/.exec(block);
    assert.ok(q,'1040 px media query');
    assert.match(q[1],/\.mst-list \.rms-grid \{ grid-template-columns: repeat\(auto-fit, minmax\(min\(240px, 100%\), 1fr\)\); \}/);
  });
  test('[V14-3] the tablet rules add no hex colour',()=>{
    assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(block),'no hex literal');
  });
}
