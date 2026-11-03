// F7-R1 · golden-file harness (RPT-ARCHITECTURE §9). For every registered report: screen cells = Excel cells (unzipped sheet XML from buildXlsx)
// = print text (PrintModel), per role; the screen cells are compared with tests/rpt-golden/goldens/<id>.json (drift fails).
// Accept a change: `node tests/rpt-golden/accept.mjs` (rewrites the goldens) and commit them. Report lanes may add cases to a golden file
// ({uid, params} entries without `tables`); the next accept fills them.
import {readFileSync,writeFileSync,existsSync,readdirSync} from 'node:fs';
export const GOLDEN_DIR=new URL('./goldens/',import.meta.url);
const gpath=id=>new URL(id+'.json',GOLDEN_DIR);

/** Entries of a stored (method 0) zip. */
export function unzipStored(buf){
  const b=Buffer.from(buf);const out={};let i=0;
  while(i+30<=b.length&&b.readUInt32LE(i)===0x04034b50){
    const method=b.readUInt16LE(i+8),size=b.readUInt32LE(i+18),nl=b.readUInt16LE(i+26),xl=b.readUInt16LE(i+28);
    const name=b.toString('utf8',i+30,i+30+nl);if(method!==0)throw new Error('zip entry not stored: '+name);
    const start=i+30+nl+xl;out[name]=b.toString('utf8',start,start+size);i=start+size;}
  return out;
}
const unesc=t=>t.replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&amp;/g,'&');
/** Sheet XML → rows of cell values (strings, numbers). Empty cells are null; gaps are filled by column letter. */
export function sheetCells(xml){
  const rows=[];const colIdx=ref=>{let n=0;for(const ch of ref.replace(/\d+/g,''))n=n*26+ch.charCodeAt(0)-64;return n-1;};
  for(const rm of xml.matchAll(/<row r="\d+">(.*?)<\/row>/gs)){
    const row=[];for(const cm of rm[1].matchAll(/<c r="([A-Z]+\d+)"[^>]*?(?:\/>|>(.*?)<\/c>)/gs)){
      const i=colIdx(cm[1]);while(row.length<i)row.push(null);const inner=cm[2]??'';
      const t=/<t[^>]*>(.*?)<\/t>/s.exec(inner),v=/<v>(.*?)<\/v>/s.exec(inner);row.push(t?unesc(t[1]):v?Number(v[1]):null);}
    rows.push(row);}
  return rows;
}
/** Typed screen value → what the Excel sheet must hold (₹ numbers in rupees, percent fraction, date serial). */
const excelOf=(c,v)=>{if(v===null||v===undefined||v==='')return null;if(typeof v==='string')return c.type==='date'&&/^\d{4}-\d{2}-\d{2}$/.test(v)?Math.round(Date.parse(v+'T00:00:00Z')/86400000)+25569:v;
  if(c.type==='money')return +(v/100).toPrecision(15);if(c.type==='pct')return +(v/100).toPrecision(15);if(c.type==='bool')return v?'Yes':'No';return v;};
export const screenTables=(M,r)=>M.rptTables(r).map(t=>({key:t.key,title:t.title??null,columns:t.columns.map(c=>c.key),rows:t.rows.map(x=>[x.kind,...t.columns.map(c=>x.cells[c.key]??null)])}));
/** Asserts the three renderers agree for one result. Returns the screen tables. */
export function threeWay(M,assert,r,property='Test property'){
  const tables=M.rptTables(r);
  // Excel
  const files=unzipStored(M.buildXlsx(M.toXlsx(r,property)));
  tables.forEach((t,i)=>{const cells=sheetCells(files[`xl/worksheets/sheet${i+1}.xml`]);
    assert.deepEqual(cells[0],t.columns.map(c=>c.label),r.id+' xlsx header '+i);
    t.rows.forEach((x,j)=>{const want=t.columns.map(c=>excelOf(c,x.cells[c.key]));const got=cells[j+1]??[];while(got.length<want.length)got.push(null);
      assert.deepEqual(got.slice(0,want.length),want,`${r.id} xlsx ${t.key} row ${j}`);});
    assert.equal(cells.length,t.rows.length+1,r.id+' xlsx row count');});
  assert.ok(files[`xl/worksheets/sheet${tables.length+1}.xml`].includes('Date basis'),r.id+' About sheet');
  // Print: every screen row appears, in order, as the same text
  const text=M.printText(M.toPrintModel(r,property));
  const lines=text.split('\n').filter(l=>!/^Page \d+ of \d+$/.test(l)&&l!=='\f');
  const want=tables.flatMap(t=>t.rows.map(x=>t.columns.map(c=>M.cellText(c,x.cells[c.key])).join('\t')));
  const body=lines.filter(l=>!tables.some(t=>l===t.columns.map(c=>c.label).join('\t')));
  assert.deepEqual(body,want,r.id+' print text');
  return screenTables(M,r);
}
const normal=x=>JSON.parse(JSON.stringify(x));
/** One line per row / column list: small diffs when a figure moves. */
export const goldenText=o=>JSON.stringify(o,null,1).replace(/\[\n\s+([^\[\]{}]*?)\n\s*\]/g,(m,inner)=>'['+inner.replace(/\n\s*/g,'')+']')+'\n';
/** Golden cases of a report: the stored file's cases, or one default case (Owner, BASE, default parameters). */
export function casesOf(id){
  if(!existsSync(gpath(id)))return [{uid:'owner',scenario:'BASE',params:{}}];
  return JSON.parse(readFileSync(gpath(id),'utf8')).cases.map(c=>({uid:c.uid,scenario:c.scenario??'BASE',params:c.params??{},tables:c.tables}));
}
export function runCase(M,c){const s=c.scenario==='BASE'?M.seed():M.loadScenario(c.scenario);return M.runReport(s,c.uid,c.id,c.params,{all:true});}
/** Drift check for every registered report (accept = rewrite). Returns failures as text. */
export function goldenCheck(M,assert,{accept=false}={}){
  const fails=[];
  for(const d of M.allReports()){
    const cases=casesOf(d.id).map(c=>({...c,id:d.id}));const out=[];
    for(const c of cases){
      const r=runCase(M,c);const tables=normal(threeWay(M,assert,r));
      out.push({uid:c.uid,scenario:c.scenario,params:c.params,tables});
      if(!accept){if(!c.tables)fails.push(`${d.id}: no golden for ${JSON.stringify(c.params)} (run node tests/rpt-golden/accept.mjs)`);
        else try{assert.deepEqual(tables,c.tables);}catch{fails.push(`${d.id} ${c.uid} ${JSON.stringify(c.params)}: drift from the golden`);}}
    }
    if(accept)writeFileSync(gpath(d.id),goldenText({id:d.id,cases:out}));
  }
  if(!accept)for(const f of readdirSync(GOLDEN_DIR).filter(f=>f.endsWith('.json')))if(!M.allReports().some(d=>d.id+'.json'===f))fails.push(`golden ${f} has no registered report`);
  return fails;
}
