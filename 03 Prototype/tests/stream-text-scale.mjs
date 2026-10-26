// CP-2 · text-size build transform (tools/cssTextScale.mjs). Names prefixed [TS]. The test() runner is synchronous, so the
// CSS is loaded once at module level (top-level await) and every check below is plain sync code.
import {readFileSync,existsSync} from 'node:fs';
import {scaleFontSizes,textScaleCss,untransformedFontSizes,isPrintMedia,readStyleSources,buildBundleCss,PRINT_RESET} from '../tools/cssTextScale.mjs';
import {nowrapRisks,styleRules,fixedHeightText} from '../tools/nowrapRisks.mjs';

const sources=await readStyleSources('.');
const sourceCss=sources.join('\n');
const bundle=await buildBundleCss('.');
const sc=n=>`calc(${n}px * var(--hcc-text,1))`;

export function register({test,assert}){
  test('[TS] simple px font size becomes calc(N px * var(--hcc-text,1))',()=>{
    assert.equal(scaleFontSizes('.a{font-size:13px}'),`.a{font-size:${sc(13)}}`);});
  test('[TS] decimal sizes (12.5px, .5px style) are rewritten with the number intact',()=>{
    assert.equal(scaleFontSizes('.a{font-size:12.5px;color:red}'),`.a{font-size:${sc(12.5)};color:red}`);
    assert.equal(scaleFontSizes('.a{font-size:.75px}'),`.a{font-size:${sc('.75')}}`);});
  test('[TS] spaced declarations keep their spacing: "font-size: 14px" and "font-size : 14px"',()=>{
    assert.equal(scaleFontSizes('.a{font-size: 14px}'),`.a{font-size: ${sc(14)}}`);
    assert.equal(scaleFontSizes('.a{font-size : 14px}'),`.a{font-size : ${sc(14)}}`);});
  test('[TS] !important is kept (tight and spaced)',()=>{
    assert.equal(scaleFontSizes('.a{font-size:16px!important}'),`.a{font-size:${sc(16)}!important}`);
    assert.equal(scaleFontSizes('.a{font-size: 16px !important}'),`.a{font-size: ${sc(16)} !important}`);});
  test('[TS] rules inside @media (width) and @supports are rewritten',()=>{
    assert.equal(scaleFontSizes('@media (max-width:640px){.a{font-size:15px}}'),`@media (max-width:640px){.a{font-size:${sc(15)}}}`);
    assert.equal(scaleFontSizes('@media screen and (min-width:900px){@supports (display:grid){.a{font-size:11px}}}'),`@media screen and (min-width:900px){@supports (display:grid){.a{font-size:${sc(11)}}}}`);});
  test('[TS] @media print blocks are untouched, rules after them are still rewritten',()=>{
    const css='.a{font-size:12px}@media print{.a{font-size:10px}.b{font-size:11px}}.c{font-size:9px}';
    assert.equal(scaleFontSizes(css),`.a{font-size:${sc(12)}}@media print{.a{font-size:10px}.b{font-size:11px}}.c{font-size:${sc(9)}}`);});
  test('[TS] print detection: "print and (...)", "screen, print" and nested print stay px; "not print" and "screen" scale',()=>{
    assert.ok(isPrintMedia('@media print')&&isPrintMedia('@media print and (max-width:500px)')&&isPrintMedia('@media screen, print'));
    assert.ok(!isPrintMedia('@media not print')&&!isPrintMedia('@media screen')&&!isPrintMedia('@media (min-width:1px)')&&!isPrintMedia('.print{}'));
    assert.equal(scaleFontSizes('@media print{@media (min-width:1px){.a{font-size:10px}}}'),'@media print{@media (min-width:1px){.a{font-size:10px}}}');
    assert.equal(scaleFontSizes('@media not print{.a{font-size:10px}}'),`@media not print{.a{font-size:${sc(10)}}}`);});
  test('[TS] already-transformed CSS is left alone (idempotent)',()=>{
    const once=scaleFontSizes('.a{font-size:13px}.b{font:600 12px/1.2 mono}@media print{.c{font-size:10px}}');
    assert.equal(scaleFontSizes(once),once);
    assert.equal(textScaleCss(textScaleCss('.a{font-size:13px}')),textScaleCss('.a{font-size:13px}'));
    assert.equal(untransformedFontSizes(once).length,0);});
  test('[TS] rem, em, %, pt, keywords, var() and calc() font sizes are untouched',()=>{
    const css='.a{font-size:1.5rem}.b{font-size:.9em}.c{font-size:90%}.d{font-size:11pt}.e{font-size:small}.f{font-size:var(--x)}.g{font-size:calc(100% - 2px)}.h{font-size:inherit}';
    assert.equal(scaleFontSizes(css),css);});
  test('[TS] line-height, min-height, paddings, widths and letter-spacing are untouched',()=>{
    const css='.a{line-height:20px;min-height:48px;padding:12px 14px;width:140px;max-width:180px;letter-spacing:2px;height:34px}';
    assert.equal(scaleFontSizes(css),css);});
  test('[TS] minified one-line CSS: every size rewritten, neighbouring declarations intact',()=>{
    const css='.a{color:#fff;font-size:14px;margin:0}.b{font-weight:700;font-size:10px!important}.c:hover{font-size:11.5px}';
    assert.equal(scaleFontSizes(css),`.a{color:#fff;font-size:${sc(14)};margin:0}.b{font-weight:700;font-size:${sc(10)}!important}.c:hover{font-size:${sc('11.5')}}`);});
  test('[TS] comments and strings are copied verbatim; names that merely end in font-size are not rewritten',()=>{
    const css='/* font-size:12px */.a::after{content:"font-size:12px"}.b{--card-font-size:12px;-x-font-size:12px}';
    assert.equal(scaleFontSizes(css),css);});
  test('[TS] the font: shorthand size token is rewritten and the line-height and family are kept',()=>{
    assert.equal(scaleFontSizes('.a{font:12px/1.45 Consolas,monospace}'),`.a{font:${sc(12)}/1.45 Consolas,monospace}`);
    assert.equal(scaleFontSizes('.a{font:600 12.5px/1.2 ui-monospace}'),`.a{font:600 ${sc('12.5')}/1.2 ui-monospace}`);
    assert.equal(scaleFontSizes('.a{font:inherit}'),'.a{font:inherit}');});
  test('[TS] an explicit max(16px, calc(16px * var(--hcc-text,1))) is left exactly as written',()=>{
    const css='.a{font-size:max(16px, calc(16px * var(--hcc-text,1)))}';assert.equal(scaleFontSizes(css),css);});
  test('[TS] textScaleCss appends the print reset exactly once',()=>{
    const out=textScaleCss('.a{font-size:13px}');
    assert.equal(out.split(PRINT_RESET).length-1,1);
    assert.ok(PRINT_RESET.includes('--hcc-text:1!important')&&PRINT_RESET.startsWith('@media print'));});

  // ---- whole bundle -------------------------------------------------------------------------------------------------
  test('[TS] whole bundle: no plain px font size survives outside print blocks, and the source had >1,000 of them',()=>{
    assert.ok(untransformedFontSizes(sourceCss).length>1000,'premise: the sources are full of plain px sizes');
    assert.deepEqual(untransformedFontSizes(bundle),[]);
    assert.equal((bundle.match(/var\(--hcc-text,1\)/g)||[]).length,untransformedFontSizes(sourceCss).length+1/* the y-a11y max() */);});
  test('[TS] whole bundle: print blocks keep their px sizes and the bundle ends with the print reset',()=>{
    for(const frag of ['.invoice .footnote{font-size:10px}','.chn-table{font-size:10px}','.grp-print-doc .grp-table{font-size:11px}','.gsv-report table{font-size:10px}','.invoice-stamp{border:2px dashed #7a1f2b;font-size:11px}','.mnt-print-head{display:flex;flex-direction:column;gap:4px;margin-bottom:14px;font-size:12px}','font-size:10.5pt!important'])
      assert.ok(bundle.includes(frag),'print fragment kept: '+frag);
    assert.ok(bundle.trimEnd().endsWith(PRINT_RESET));});
  test('[TS] phone form minimum: y-a11y forces max(16px, calc(16px * var(--hcc-text,1))) on inputs, selects and textareas — never below 16 px at 0.9',()=>{
    const rule='.m-shell input:not([type="checkbox"]):not([type="radio"]),.m-shell select,.m-shell textarea{font-size:max(16px, calc(16px * var(--hcc-text,1)))}';
    assert.ok(readFileSync('src/styles/y-a11y.css','utf8').includes(rule),'source rule');
    assert.equal(bundle.split(rule).length-1,1,'bundle rule, once');
    const px=scale=>Math.max(16,16*scale);
    for(const [s,want] of [[0.9,16],[1,16],[1.15,18.4],[1.3,20.8]])assert.ok(Math.abs(px(s)-want)<1e-9,`scale ${s}`);});
  test('[TS] bundle size growth is under 60 KB',()=>{
    const grow=Buffer.byteLength(bundle)-Buffer.byteLength(sourceCss);
    assert.ok(grow>0&&grow<60*1024,'growth '+grow+' bytes');});
  test('[TS] dist/index.html (when built with this transform) has no plain px font size in its <style> outside print',()=>{
    for(const f of ['dist/index.html','dist/production/index.html']){
      if(!existsSync(f))continue;const html=readFileSync(f,'utf8');
      if(!html.includes('--hcc-text:1!important'))continue;                    // a stale pre-CP-2 build: nothing to assert yet
      const style=/<style>([\s\S]*?)<\/style>/.exec(html)[1];
      assert.deepEqual(untransformedFontSizes(style),[],f);
      assert.ok(style.includes(PRINT_RESET),f+' print reset');}});
  test('[TS] build.mjs uses the shared transform and serve.mjs serves the built file (so the dev server sees the same CSS)',()=>{
    assert.match(readFileSync('build.mjs','utf8'),/buildBundleCss/);
    assert.match(readFileSync('serve.mjs','utf8'),/dist\/index\.html/);});

  // ---- browser-free layout risk scan ------------------------------------------------------------------------------------
  test('[TS] layout risk scan: the parser skips print rules and finds the known fixed-box nowrap rules',()=>{
    const rules=styleRules('.a{white-space:nowrap}@media print{.p{white-space:nowrap;width:10px}}@media (max-width:600px){.b{width:5px}}');
    assert.deepEqual(rules.map(r=>r.selector),['.a','.b']);
    const risks=nowrapRisks(sourceCss).map(r=>r.selector);
    assert.ok(risks.includes('.property strong')&&risks.includes('.demo-sub'));
    assert.ok(fixedHeightText(sourceCss).some(r=>r.selector==='.m-avatar'));});
}
