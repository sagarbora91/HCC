// CP-3b · colour-token migration of the module stylesheets m-*.css (tools/cssTokens.mjs). Names prefixed [CT-B].
// Proof of "no visible change": resolving every var(--c-…) / semantic variable back to its hex gives the ORIGINAL css (git 799e4cc).
import {readFileSync,existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {tokenizeColours,resolveTokens,canonHex,SEMANTIC_TOKENS,TOKEN_SHEET,TOKEN_HEADER,tokenSheetFile} from '../tools/cssTokens.mjs';
import {buildBundleCss} from '../tools/cssTextScale.mjs';
import {ROLE_LIGHT} from '../tools/themes.mjs';

const BASE='799e4cc';
const FILES=['m-channels','m-pos','m-staff','m-yield','m-messaging','m-groups','m-assistant','m-handover','m-maintenance','m-inventory','m-accounting','m-compliance','m-config','m-cashbook'];
const ws=s=>s.replace(/\s+/g,' ').trim();
const original=f=>execFileSync('git',['show',`${BASE}:03 Prototype/src/styles/${f}.css`],{encoding:'utf8',maxBuffer:1<<24});
const current=f=>readFileSync(`src/styles/${f}.css`,'utf8');
const sheet=readFileSync(TOKEN_SHEET,'utf8'); // CP-5: one merged sheet (0-tokens.css) and one tokenizer (tools/cssTokens.mjs)
const tokens=new Map([...sheet.matchAll(/^\s*(--c-[0-9a-f]+):(#[0-9a-f]+);$/gm)].map(m=>[m[1],m[2]]));
const stripCss=s=>s.replace(/\/\*[\s\S]*?\*\//g,'').replace(/url\([^)]*\)/g,'');
const used=css=>new Set([...css.matchAll(/var\((--c-[0-9a-f]+)\)/g)].map(m=>m[1]));
const bundle=await buildBundleCss('.');
const dist=existsSync('dist/index.html')?readFileSync('dist/index.html','utf8'):'';

export function register({test,assert}){
  test('[CT-B] every module sheet resolves back to its original css (zero visible change)',()=>{
    for(const f of FILES){
      const back=resolveTokens(current(f),new Map([...SEMANTIC_TOKENS,...tokens,...ROLE_LIGHT])); // CP-5 roles resolve to the hex they replaced
      assert.equal(ws(canonHex(back)),ws(canonHex(original(f))),f+' differs from '+BASE);
    }});
  test('[CT-B] no raw hex colour remains outside url(...) and comments in any module sheet',()=>{
    for(const f of FILES)assert.deepEqual(stripCss(current(f)).match(/#[0-9a-fA-F]{3,8}\b/g)||[],[],f);});
  test('[CT-B] every var(--c-…) used by the module sheets is defined in 0-tokens.css',()=>{
    for(const f of FILES)for(const v of used(current(f)))assert.ok(tokens.has(v),`${f}: ${v} undefined`);});
  test('[CT-B] token sheet: one :root block, sorted keys, one declaration per line, header comment, no unused token',()=>{
    assert.ok(sheet.startsWith(TOKEN_HEADER+'\n'));
    assert.equal((sheet.match(/:root\s*\{/g)||[]).length,1);
    const keys=[...tokens.keys()];
    assert.deepEqual(keys,[...keys].sort());
    assert.equal(keys.length,sheet.split('\n').filter(l=>/^\s*--c-/.test(l)).length);
    const all=new Set(FILES.flatMap(f=>[...used(current(f))]));
    for(const k of all)assert.ok(tokens.has(k),k); // unused-anywhere is checked over every stylesheet in [CT-A]
    assert.equal(sheet,tokenSheetFile(tokens));});
  test('[CT-B] token values are the hex implied by their name (--c-<key> -> #<key>)',()=>{
    for(const [k,v] of tokens)assert.equal(v,'#'+k.slice(4),k);});
  test('[CT-B] colours equal to the styles.css :root semantic values use the semantic variable, not --c-…',()=>{
    for(const hex of ['7a1f2b','5e1620','f5e4e6','2b1d16','6b5547','e6d9c8','faf6f0','f6efe6','b5541e'])assert.ok(!tokens.has('--c-'+hex),hex);
    assert.ok(FILES.some(f=>/var\(--ink\)/.test(current(f)))&&FILES.some(f=>/var\(--primary\)/.test(current(f))));});
  test('[CT-B] alpha hex keeps its alpha digits (#2b1d1626 -> --c-2b1d1626) and 3/4-digit hex expands',()=>{
    assert.ok(tokens.get('--c-2b1d1626')==='#2b1d1626');
    assert.ok(tokens.get('--c-ffffffaa')==='#ffffffaa'&&/var\(--c-ffffffaa\)/.test(current('m-messaging')));
    assert.ok(tokens.has('--c-ffffff'));});
  test('[CT-B] non-hex colours (transparent, rgb/hsl) are left exactly as written',()=>{
    for(const f of FILES){
      const o=(original(f).match(/\b(transparent|currentColor|white|black|inherit)\b|(rgba?|hsla?)\(/gi)||[]).length;
      const n=(current(f).match(/\b(transparent|currentColor|white|black|inherit)\b|(rgba?|hsla?)\(/gi)||[]).length;
      assert.equal(n,o,f);}});
  test('[CT-B] only colour literals changed: same line count and every changed line contains a hex in the original',()=>{
    for(const f of FILES){
      const o=original(f).split('\n'),n=current(f).split('\n');
      assert.equal(n.length,o.length,f);
      for(let i=0;i<o.length;i++)if(o[i]!==n[i])assert.ok(/#[0-9a-fA-F]{3,8}\b/.test(o[i]),`${f}:${i+1}`);
      assert.equal(original(f).includes('\r'),current(f).includes('\r'),f+' line endings');}});
  test('[CT-B] tokenizer: skips url(), comments, strings, selectors (#id) and :root definitions; expands and maps semantics',()=>{
    const src='/* #fff */:root{--x:#abc;--y:#123456}#abc{color:#ABC;background:url(#fff);border:1px solid #7a1f2b;content:"#fff";box-shadow:0 0 1px #5e162050;fill:#FFFA;outline:1px solid rgb(1,2,3)}';
    const r=tokenizeColours(src);
    assert.equal(r.css,'/* #fff */:root{--x:#abc;--y:#123456}#abc{color:var(--c-aabbcc);background:url(#fff);border:1px solid var(--primary);content:"#fff";box-shadow:0 0 1px var(--c-5e162050);fill:var(--c-ffffffaa);outline:1px solid rgb(1,2,3)}');
    assert.deepEqual([...r.tokens],[['--c-5e162050','#5e162050'],['--c-aabbcc','#aabbcc'],['--c-ffffffaa','#ffffffaa']]); // sorted by name (merged tokenizer)
    assert.equal(tokenizeColours(r.css).css,r.css);
    assert.equal(canonHex(resolveTokens(r.css,r.tokens)),canonHex(src));});
  test('[CT-B] the css bundle (and dist/index.html when built) defines every --c-… token the module sheets use',()=>{
    for(const [name,css] of [['bundle',bundle],['dist',dist]]){
      if(!css)continue;
      for(const f of FILES)for(const v of used(current(f)))assert.ok(css.includes(v+':#'),`${name}: ${v} not defined`);}
    assert.ok(/--c-ffffff:#ffffff/.test(bundle));});
}
