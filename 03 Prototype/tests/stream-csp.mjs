// A10-05 · Content-Security-Policy <meta> of the production build (tools/csp.mjs, build.mjs). Names prefixed [CSP A10-05].
// The APK serves dist/production/index.html from Capacitor's local asset server (no response header), so the page carries its own policy;
// the server's header (08 Production/src/Hcc.Api/Hosting/WebAppHosting.cs) still applies on top in a browser.
import {readFileSync,existsSync} from 'node:fs';
import {addCspMeta,checkCspMeta,parsePolicy,sha256Source,inlineBlocks,productionCsp} from '../tools/csp.mjs';

const PAGE='<!doctype html><html lang="en"><head><meta charset="UTF-8"><title>t</title><style>body{margin:0}</style></head><body><div id="root"></div><script>window.hcc=1;</script></body></html>';
const metaPolicy=html=>parsePolicy(/<meta http-equiv="Content-Security-Policy" content="([^"]*)">/.exec(html)[1]);
/** The server header's fixed directives, read from WebAppHosting.ContentSecurityPolicy (string literals of its return statement). */
function serverPolicy(){
  const cs=readFileSync('../08 Production/src/Hcc.Api/Hosting/WebAppHosting.cs','utf8');
  const body=cs.slice(cs.indexOf('public static string ContentSecurityPolicy'));const at=body.indexOf('return "'),ret=body.slice(at,body.indexOf('}',at));
  return parsePolicy([...ret.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map(m=>m[1]).join(''));
}

export function register({test,assert}){
  test('[CSP A10-05] known answers: SHA-256 over the UTF-8 bytes, as the browser reports it (same values pinned in the server test)',()=>{
    assert.equal(sha256Source('window.hcc=1;'),"'sha256-wPLFDDlm0UuafW5zggSwu3dtgfgDZ0XpvOXam9dYF88='");
    assert.equal(sha256Source("window.r='₹ ·';"),"'sha256-ocHLj6bOal4ZQL+5Og9MaBAMJpfXFKuP6jIxXg43t6I='");
    assert.equal(sha256Source('body{margin:0}'),"'sha256-IAdwN3biDCQ3brrgp1m8kBEsPQYyqfREKOEfeoREopc='");});
  test('[CSP A10-05] the meta goes right after <meta charset>, before every style and script, and allows exactly their hashes',()=>{
    const html=addCspMeta(PAGE),p=metaPolicy(html);
    assert.ok(html.startsWith('<!doctype html><html lang="en"><head><meta charset="UTF-8"><meta http-equiv="Content-Security-Policy" content="'));
    assert.deepEqual(p['script-src'],["'sha256-wPLFDDlm0UuafW5zggSwu3dtgfgDZ0XpvOXam9dYF88='"]);
    assert.deepEqual(p['style-src'],["'sha256-IAdwN3biDCQ3brrgp1m8kBEsPQYyqfREKOEfeoREopc='",'data:']);
    assert.deepEqual(checkCspMeta(html),[]);
    assert.equal(html.replace(/<meta http-equiv="Content-Security-Policy" content="[^"]*">/,''),PAGE,'nothing else in the page changes');});
  test('[CSP A10-05] lock-down directives: nothing unsafe, no plugins, no <base>, HTTPS only for the hotel server, no meta-ignored directives',()=>{
    const p=metaPolicy(addCspMeta(PAGE)),all=Object.values(p).flat();
    assert.ok(!all.some(t=>/unsafe-(inline|eval|hashes)|^\*$|^http:$/.test(t)),all.join(' '));
    for(const d of ['default-src','object-src','base-uri'])assert.deepEqual(p[d],["'none'"],d);
    assert.deepEqual(p['connect-src'],["'self'",'https:']);
    assert.deepEqual(p['img-src'],["'self'",'data:','blob:'],'slip photos are blob:/data: images');
    assert.deepEqual(p['worker-src'],["'self'",'blob:'],'the scenario worker starts from a Blob URL');
    for(const d of ['frame-ancestors','sandbox','report-uri'])assert.ok(!(d in p),d+' is ignored in a meta policy');
    assert.equal(parsePolicy(productionCsp([],[]))['script-src'].join(' '),"'none'",'a page without scripts allows none');});
  test('[CSP A10-05] the check refuses: no meta, two metas, a stale script hash, an extra hash, unsafe-inline, a meta after the script, CR in a block',()=>{
    const good=addCspMeta(PAGE);
    assert.match(checkCspMeta(PAGE).join(),/no Content-Security-Policy meta/);
    assert.match(checkCspMeta(good.replace('<title>',good.match(/<meta http-equiv[^>]*>/)[0]+'<title>')).join(),/more than one/);
    assert.match(checkCspMeta(good.replace('window.hcc=1;','window.hcc=2;')).join(),/does not allow the page's inline <script>/);
    assert.match(checkCspMeta(good.replace("script-src '","script-src 'sha256-AAAA=' '")).join(),/matches no inline <script>/);
    assert.match(checkCspMeta(good.replace("script-src '","script-src 'unsafe-inline' '")).join(),/by hash only/);
    assert.match(checkCspMeta(good.replace("style-src '","style-src 'unsafe-inline' '")).join(),/by hash only/);
    assert.match(checkCspMeta(good.replace("connect-src 'self' https:","connect-src *")).join(),/connect-src/);
    assert.match(checkCspMeta(good.replace("object-src 'none'","object-src 'self'")).join(),/object-src/);
    const meta=good.match(/<meta http-equiv[^>]*>/)[0];
    assert.match(checkCspMeta(good.replace(meta,'').replace('</body>',meta+'</body>')).join(),/inside <head>|before every inline/);
    assert.match(checkCspMeta(good.replace('margin:0}','margin:0}\r\n')).join(),/CR or NUL/);});
  test('[CSP A10-05] addCspMeta refuses a page that already has a policy or has no <meta charset>',()=>{
    assert.throws(()=>addCspMeta(addCspMeta(PAGE)),/already carries/);
    assert.throws(()=>addCspMeta(PAGE.replace('<meta charset="UTF-8">','')),/no <meta charset>/);
    assert.throws(()=>addCspMeta(PAGE.replace('margin:0}','margin:0}\r\n')),/CR or NUL/);});
  test('[CSP A10-05] block matching follows the server: a script body runs to its first </script>, tags inside it are not blocks',()=>{
    const b=inlineBlocks('<style>a{}</style><script>var s="<style>x</style>";</script>');
    assert.deepEqual(b.map(x=>[x.tag,x.body]),[['style','a{}'],['script','var s="<style>x</style>";']]);});
  test('[CSP A10-05] meta and server header (WebAppHosting.cs) agree on every fixed directive; the meta is stricter for script/style and opens connect-src to https: only',()=>{
    const server=serverPolicy(),meta=metaPolicy(addCspMeta(PAGE));
    assert.ok(Object.keys(server).length>=12,'parsed the server policy: '+Object.keys(server).join(' '));
    for(const [d,v] of Object.entries(server)){
      if(d==='frame-ancestors'){assert.ok(!(d in meta));continue;}                       // header only (ignored in a meta)
      if(d==='script-src'||d==='style-src'){assert.ok(meta[d].every(t=>/^'sha256-/.test(t)||t==='data:'),d);continue;} // hashes instead of 'self' / 'unsafe-inline'
      if(d==='connect-src'){assert.deepEqual(meta[d],[...v,'https:']);continue;}
      assert.deepEqual(meta[d],v,d);}
    for(const d of Object.keys(meta))assert.ok(d in server,'the server header also sets '+d);});
  test('[CSP A10-05] build.mjs adds the meta to the production build only',()=>{
    const b=readFileSync('build.mjs','utf8');
    assert.match(b,/import \{addCspMeta\} from '\.\/tools\/csp\.mjs'/);
    assert.match(b,/const html=production\?addCspMeta\(page\):page;/);});
  test('[CSP A10-05] dist/production/index.html (when built) carries a valid CSP meta whose hashes match its inline script and style (smoke check)',()=>{
    const f='dist/production/index.html';if(!existsSync(f))return;
    const html=readFileSync(f,'utf8');
    assert.deepEqual(checkCspMeta(html),[]);
    const blocks=inlineBlocks(html),p=metaPolicy(html);
    assert.deepEqual(blocks.map(b=>b.tag),['style','script'],'one inline style, one inline script');
    assert.deepEqual(p['script-src'],[sha256Source(blocks[1].body)]);
    assert.deepEqual(p['style-src'],[sha256Source(blocks[0].body),'data:']);
    assert.ok(!/\r/.test(html),'no CR anywhere in the production page');});
}
