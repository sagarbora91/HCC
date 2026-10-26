// A10-05: Content-Security-Policy <meta> for the production single-file build (dist/production/index.html).
// The server already sends a CSP header for the browser app (08 Production/src/Hcc.Api/Hosting/WebAppHosting.cs), but inside the APK the
// same file is served by Capacitor's local asset server (https://localhost) with no header, so the page carries its own policy.
// Both policies apply when the server serves the page (a browser enforces every policy it gets), so the meta must allow what the web app
// needs and the header narrows it further (connect-src 'self', frame-ancestors 'none').
//
// - Scripts and styles: the build inlines exactly one <script> and one <style>; both are allowed by their SHA-256 only (no
//   'unsafe-inline', no 'unsafe-eval', no host). React sets style properties through the CSSOM (element.style), which CSP does not
//   restrict, so no 'unsafe-inline' is needed for styles. The empty `@import url('data:text/css,')` at the top of the CSS needs data:.
// - Capacitor's bridge script is added by the APK (WebViewCompat.addDocumentStartJavaScript, or on an old WebView inserted right after
//   <head>, i.e. before this meta) and is therefore not affected; native → JS calls use evaluateJavascript.
// - connect-src: the hotel server's address is typed on the tablet at run time, so a build-time policy can only say "https:". The APK
//   normally talks to it through CapacitorHttp (native; not governed by the page CSP); web fetch is the fallback. In a browser the
//   server header narrows connect-src to the page's own origin.
// - frame-ancestors, report-uri and sandbox are ignored in a <meta> policy (CSP3), so frame-ancestors stays in the server header only.
// The hash rules are the server's (WebAppHosting.ContentSecurityPolicy): each <script>/<style> block, matched in document order with the
// body running to the first matching close tag, hashed over its UTF-8 bytes.
import {createHash} from 'node:crypto';

const BLOCK=/<(script|style)([^>]*)>([\s\S]*?)<\/\1>/gi;
const META=/<meta\s+http-equiv="Content-Security-Policy"\s+content="([^"]*)"\s*\/?>/gi;

/** Inline <script>/<style> blocks in document order (same match as the server: a script body runs to the first </script>). */
export function inlineBlocks(html){return [...html.matchAll(BLOCK)].map(m=>({tag:m[1].toLowerCase(),attrs:m[2],body:m[3],index:m.index}));}
/** CSP source expression for a block body: 'sha256-<base64 of SHA-256 over the UTF-8 bytes>'. */
export const sha256Source=body=>`'sha256-${createHash('sha256').update(body,'utf8').digest('base64')}'`;

/** The production policy for the given script / style hash sources. */
export function productionCsp(scriptHashes,styleHashes){
  return [
    "default-src 'none'",
    `script-src ${scriptHashes.length?scriptHashes.join(' '):"'none'"}`,
    `style-src ${[...styleHashes,'data:'].join(' ')}`,
    "img-src 'self' data: blob:", // slip photos (blob:/data:), the inline icons
    "font-src 'self' data:",
    "connect-src 'self' https:",  // the hotel server (address typed at run time; HTTPS only)
    "media-src 'self' blob:",
    "worker-src 'self' blob:",    // the inlined scenario worker starts from a Blob URL
    "manifest-src 'self' data:",  // the web-app manifest is a data: URL
    "object-src 'none'",
    "base-uri 'none'",
    "form-action 'self'",
  ].join('; ');
}

/** Parses a policy into {directive: [tokens]} (first occurrence wins, as in browsers). */
export function parsePolicy(policy){
  const out={};
  for(const part of policy.split(';')){const [name,...tokens]=part.trim().split(/\s+/);if(name&&!(name.toLowerCase() in out))out[name.toLowerCase()]=tokens.filter(Boolean);}
  return out;
}

/** Inserts the CSP <meta> right after <meta charset> (before every <style> and <script>). Throws when the page already has one. */
export function addCspMeta(html){
  if(/http-equiv=["']?Content-Security-Policy/i.test(html))throw new Error('the page already carries a Content-Security-Policy meta');
  const charset=html.match(/<meta charset="[^"]*">/i);
  if(!charset)throw new Error('no <meta charset> in the page: cannot place the Content-Security-Policy meta');
  const blocks=inlineBlocks(html);
  const meta=`<meta http-equiv="Content-Security-Policy" content="${productionCsp(blocks.filter(b=>b.tag==='script').map(b=>sha256Source(b.body)),blocks.filter(b=>b.tag==='style').map(b=>sha256Source(b.body)))}">`;
  const at=charset.index+charset[0].length;
  const out=html.slice(0,at)+meta+html.slice(at);
  const problems=checkCspMeta(out);if(problems.length)throw new Error('CSP meta check failed: '+problems.join('; '));
  return out;
}

/** Checks a production page: exactly one CSP meta, in <head> before every script and style, allowing exactly the page's inline scripts
 *  and styles by hash (no stale hash, nothing unsafe), with the fixed lock-down directives. Returns the problems found ([] = fine). */
export function checkCspMeta(html){
  const problems=[];const metas=[...html.matchAll(META)];
  if(metas.length!==1)return [metas.length?'the page has more than one Content-Security-Policy meta':'the page has no Content-Security-Policy meta (A10-05: run npm run build:production)'];
  const meta=metas[0],p=parsePolicy(meta[1].replace(/&#39;|&apos;/g,"'"));
  const head=html.search(/<head>/i),headEnd=html.search(/<\/head>/i),blocks=inlineBlocks(html);
  if(head<0||headEnd<0||meta.index<head||meta.index>headEnd)problems.push('the Content-Security-Policy meta must sit inside <head>');
  if(blocks.some(b=>b.index<meta.index))problems.push('the Content-Security-Policy meta must come before every inline <script> and <style>');
  for(const [tag,dir] of [['script','script-src'],['style','style-src']]){
    const tokens=p[dir]||[],own=blocks.filter(b=>b.tag===tag);
    for(const b of own){
      if(/[\r\0]/.test(b.body))problems.push(`an inline <${tag}> contains a CR or NUL character: the browser normalises it before hashing, so the hash would not match`);
      if(!tokens.includes(sha256Source(b.body)))problems.push(`${dir} does not allow the page's inline <${tag}> (hash ${sha256Source(b.body)}): the meta is stale, rebuild`);
    }
    const hashes=tokens.filter(t=>/^'sha(256|384|512)-/.test(t)),wanted=new Set(own.map(b=>sha256Source(b.body)));
    if(hashes.some(h=>!wanted.has(h)))problems.push(`${dir} allows a hash that matches no inline <${tag}> of the page`);
    const loose=tokens.filter(t=>!/^'sha(256|384|512)-/.test(t)&&!(tag==='style'&&t==='data:')&&!(tag==='script'&&t==="'none'"&&!own.length));
    if(loose.length)problems.push(`${dir} must allow the inline ${tag} by hash only, found: ${loose.join(' ')}`);
  }
  for(const [dir,want] of [['default-src',"'none'"],['object-src',"'none'"],['base-uri',"'none'"]])
    if((p[dir]||[]).join(' ')!==want)problems.push(`${dir} must be ${want}`);
  if(!p['connect-src']||p['connect-src'].some(t=>!["'self'",'https:'].includes(t)))problems.push("connect-src may name only 'self' and https: (the hotel server)");
  if('frame-ancestors' in p||'sandbox' in p||'report-uri' in p)problems.push('frame-ancestors / sandbox / report-uri are ignored in a meta policy: keep them in the server header');
  return problems;
}
