// A01: is the built HTML self-contained (no external asset references) and does the production build carry demo controls?
// Run from "03 Prototype":  node "../05 Delivery Evidence/Audit/probes/A01_selfcontained.mjs"
import { readFileSync, existsSync } from 'node:fs';

const files = ['dist/index.html', 'dist/production/index.html'];
for (const f of files) {
  const t = readFileSync(f, 'utf8');
  const count = (re) => (t.match(re) || []).length;
  console.log(`== ${f}  length=${t.length}`);
  console.log('  <script src=...>               :', count(/<script[^>]+\ssrc=/gi));
  console.log('  <link ...href=...>             :', count(/<link[^>]+\shref=/gi), (t.match(/<link[^>]+>/gi) || []).map((x) => x.slice(0, 70)).slice(0, 5));
  console.log('  <img|iframe|source|video src=http(s):', count(/<(img|iframe|source|video|audio)[^>]+src=["']?https?:/gi));
  console.log('  src|href attribute = http(s)://:', count(/\s(src|href)=["']https?:\/\//gi));
  console.log('  css url(http(s)://...)         :', count(/url\(\s*["']?https?:\/\//gi));
  console.log('  @import                        :', count(/@import/gi));
  console.log('  font/CDN hosts                 :', count(/fonts\.googleapis|fonts\.gstatic|cdnjs|jsdelivr|unpkg\.com|cdn\./gi));
  const urls = [...new Set((t.match(/https?:\/\/[A-Za-z0-9._~:\/?#@!$&*+,;=%-]+/g) || []))].sort();
  console.log('  distinct http(s):// literals   :', urls.length);
  for (const u of urls.slice(0, 40)) console.log('     ', u.slice(0, 110));
  console.log('  contains "Demo controls"       :', /Demo controls/i.test(t), '| "Preview as" :', /Preview as/.test(t), '| "scenarioSwitch"-like "Scenario" word count:', count(/Scenario/g));
}
