// CP-3c · colour tokens for the feature, masters and admin stylesheets. Names prefixed [CT-C].
// Proof of "no visible change": resolving every var(--c-…) / semantic var back to its hex gives the ORIGINAL stylesheet (from git 799e4cc).
import {readFileSync,existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {tokenizeColours,resolveTokens,SEMANTIC_TOKENS as SEMANTIC,TOKEN_SHEET,TOKEN_HEADER,parseTokenSheet} from '../tools/cssTokens.mjs';
import {buildBundleCss} from '../tools/cssTextScale.mjs';

const BASE='799e4cc';
const FILES='admin closing folio guests history housekeeping reservations mobile-admin prefs restriction m-masters m-money-masters m-roommasters m-catalogue m-services m-exports'.split(' ').map(f=>`${f}.css`);
// canonical colour spelling: #ABC and #aabbcc are the same colour, so the original's shorthand/uppercase hex is spelled the way the tokens spell it
const canon=s=>s.replace(/#([0-9a-fA-F]{3,4})(?![0-9a-zA-Z_-])/g,(m,h)=>'#'+[...h.toLowerCase()].map(c=>c+c).join('')).replace(/#[0-9a-fA-F]{6,8}(?![0-9a-zA-Z_-])/g,m=>m.toLowerCase());
const norm=s=>canon(s).replace(/\s+/g,' ').trim();
const orig=f=>execFileSync('git',['show',`${BASE}:03 Prototype/src/styles/${f}`],{encoding:'utf8',maxBuffer:1<<24});
const cur=f=>readFileSync(`src/styles/${f}`,'utf8');
const sheet=readFileSync(TOKEN_SHEET,'utf8'); // CP-5: one merged sheet (0-tokens.css) and one tokenizer (tools/cssTokens.mjs)
const defs=parseTokenSheet(sheet);
const stripUrls=css=>css.replace(/\/\*[\s\S]*?\*\//g,'').replace(/url\([^)]*\)/gi,'');
const used=css=>[...css.matchAll(/var\((--c-[0-9a-f]+)\)/g)].map(m=>m[1]);
const bundle=await buildBundleCss('.');

export function register({test,assert}){
  test('[CT-C] token sheet: one :root block, header comment, entries sorted by key, one per line',()=>{
    assert.ok(sheet.startsWith(TOKEN_HEADER+'\n:root{\n'));
    assert.equal((sheet.match(/:root\s*\{/g)||[]).length,1);
    const names=[...defs.keys()];assert.ok(names.length>50);
    assert.deepEqual(names,[...names].sort());
    assert.equal(sheet.split('\n').filter(l=>l.startsWith('--c-')).length,names.length);});
  test('[CT-C] every token value is the hex its key spells (8-digit keeps alpha) and never a semantic colour',()=>{
    for(const [k,v] of defs){assert.equal(v,'#'+k.slice(4),k);assert.ok(![...SEMANTIC.values()].includes(v),k+' duplicates a semantic variable');}});
  test('[CT-C] all 16 files: resolving tokens gives back the original stylesheet (zero visible change)',()=>{
    for(const f of FILES)assert.equal(norm(resolveTokens(cur(f),defs)),norm(resolveTokens(orig(f),new Map())),f);});
  test('[CT-C] all 16 files: re-running the tokenizer on the original reproduces the committed file exactly',()=>{
    for(const f of FILES)assert.equal(tokenizeColours(orig(f)).css,cur(f),f);});
  test('[CT-C] no raw hex colour remains outside url(...) and comments',()=>{
    for(const f of FILES)assert.deepEqual(stripUrls(cur(f)).match(/#[0-9a-fA-F]{3,8}\b/g)||[],[],f);});
  test('[CT-C] every var(--c-…) used by the 16 files is defined in 0-tokens.css (unused definitions: checked over every sheet in [CT-A])',()=>{
    const all=new Set(FILES.flatMap(f=>used(cur(f))));
    for(const k of all)assert.ok(defs.has(k),k+' undefined');
    assert.ok(all.size>50);});
  test('[CT-C] semantic colours use the styles.css variables (--primary, --ink, --line …), never a --c- duplicate',()=>{
    const root=readFileSync('src/styles.css','utf8').match(/:root\{[^}]*\}/)[0];
    for(const [n,v] of SEMANTIC)assert.ok(root.includes(`${n}:${v}`),n);
    for(const f of FILES)for(const k of used(cur(f)))assert.ok(!SEMANTIC_HEX.has(k.slice(4)),f+' '+k);});
  test('[CT-C] the changed lines differ only in colour literals (selectors, order and other values untouched)',()=>{
    for(const f of FILES){const a=orig(f).split('\n'),b=cur(f).split('\n');assert.equal(a.length,b.length,f);
      for(let i=0;i<a.length;i++)if(a[i]!==b[i])assert.equal(norm(resolveTokens(b[i],defs)),norm(resolveTokens(a[i],new Map())),f+':'+(i+1));}});
  test('[CT-C] line endings are unchanged (no CRLF introduced)',()=>{
    for(const f of FILES)assert.equal(/\r/.test(cur(f)),/\r/.test(orig(f)),f);});
  test('[CT-C] the style bundle (buildBundleCss) defines every var(--c-…) its sheets use; the token sheet loads first',()=>{
    for(const k of new Set(used(bundle)))if(FILES.some(f=>used(cur(f)).includes(k)))assert.ok(bundle.includes(`${k}:${defs.get(k)};`),k);
    assert.ok(bundle.indexOf(TOKEN_HEADER)>0&&bundle.indexOf(TOKEN_HEADER)<bundle.indexOf('.adm-perm'));});
  test('[CT-C] dist/index.html and dist/production/index.html (when built) still resolve every --c- token of this sheet',()=>{
    for(const p of ['dist/index.html','dist/production/index.html']){if(!existsSync(p))continue;
      const html=readFileSync(p,'utf8');const style=html.slice(html.indexOf('<style>'),html.indexOf('</style>'));
      for(const k of new Set(used(style)))if(defs.has(k))assert.ok(style.includes(`${k}:${defs.get(k)};`),p+' '+k);}});
  test('[CT-C] tokenizer: skips url(), comments, strings, custom-property definitions and selectors; leaves rgb()/named colours',()=>{
    const r=tokenizeColours('/* #fff */.a#abc,.b:hover #abc{color:#ABC;background:url(#fff);--x:#123456;border:1px solid #7A1F2B;box-shadow:0 0 1px rgba(0,0,0,.2);background-color:white;content:"#fff"}');
    assert.equal(r.css,'/* #fff */.a#abc,.b:hover #abc{color:var(--c-aabbcc);background:url(#fff);--x:#123456;border:1px solid var(--primary);box-shadow:0 0 1px rgba(0,0,0,.2);background-color:white;content:"#fff"}');
    assert.deepEqual(r.left,{fn:1,named:1});
    assert.equal(tokenizeColours('.a{color:#5e162050}').css,'.a{color:var(--c-5e162050)}');
    assert.equal(tokenizeColours(r.css).css,r.css);                       // idempotent
    assert.equal(resolveTokens(tokenizeColours('.a{color:#ABC}').css,new Map([['--c-aabbcc','#aabbcc']])),'.a{color:#aabbcc}');});
}
const SEMANTIC_HEX=new Set([...SEMANTIC.values()].map(v=>v.slice(1)));
