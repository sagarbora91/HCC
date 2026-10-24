// Local visual QA. Requires an existing Playwright installation; adds no app dependency.
import {build} from 'esbuild';
import {readFile,readdir,mkdir,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {createRequire} from 'node:module';
import assert from 'node:assert/strict';
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.HCC_PLAYWRIGHT_PATH||'playwright');
await build({entryPoints:['tests/print-fixture.tsx'],bundle:true,platform:'node',format:'esm',external:['react','react-dom/server','lucide-react'],outfile:'tests/.generated/print-fixture.mjs',logLevel:'error'});
const {reportHtml}=await import('./.generated/print-fixture.mjs');
const files=(await readdir('src/styles')).filter(f=>f.endsWith('.css')).sort();
const css=[await readFile('src/styles.css','utf8'),...await Promise.all(files.map(f=>readFile('src/styles/'+f,'utf8')))].join('\n');
const out=resolve('../05 Delivery Evidence/Acceptance/print-layout');await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true,...(process.env.HCC_CHROME_PATH?{executablePath:process.env.HCC_CHROME_PATH}:{})});
const results=[];
try{
  for(const [name,from,to,count] of [['short','2026-10-24','2026-10-24',1],['multi','2026-04-01','2026-06-01',62]]){
    const page=await browser.newPage({viewport:{width:794,height:1123}});
    await page.setContent(reportHtml(from,to,css));await page.emulateMedia({media:'print'});
    assert.equal(await page.locator('tbody tr').count(),count);
    const layout=await page.evaluate(()=>({root:getComputedStyle(document.documentElement).backgroundColor,
      body:getComputedStyle(document.body).backgroundColor,overflow:getComputedStyle(document.querySelector('.f-print-doc')).overflow,
      width:document.documentElement.scrollWidth,viewport:innerWidth,buttons:[...document.querySelectorAll('button')].filter(x=>x.getClientRects().length).length}));
    assert.equal(layout.root,'rgb(255, 255, 255)');assert.equal(layout.body,'rgb(255, 255, 255)');
    assert.equal(layout.overflow,'visible');assert.equal(layout.buttons,0);assert.ok(layout.width<=layout.viewport);
    await page.pdf({path:resolve(out,name+'.pdf'),preferCSSPageSize:true,printBackground:true,displayHeaderFooter:false});
    await page.screenshot({path:resolve(out,name+'-print.png'),fullPage:true});
    results.push({name,rows:count,...layout});await page.close();
  }
}finally{await browser.close();}
await writeFile(resolve(out,'layout-results.json'),JSON.stringify(results,null,2));
console.log(JSON.stringify(results,null,2));
