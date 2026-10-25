// Minimal .xlsx writer — no packages. OOXML parts inside a stored (uncompressed) ZIP.
// Rule source (read-only reference): ETP OpenXmlReportExporter.cs — header row, numbers as numbers, ₹ number format, ISO dates as date serials.
// Money in HCC is integer paise: pass {paise:n} and it is written as the rupee number n/100 with a ₹ format.

export type XCell=string|number|null|undefined|{paise:number}|{date:string}|{int:number}|/** Fraction (0.494 = 49.4%), shown with a 0.0% format. */{pct:number};
export type XSheet={name:string;rows:XCell[][];/** First row is a bold header (default true). */header?:boolean;/** Column widths in characters. */widths?:number[]};

const esc=(t:string)=>t.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g,'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
/** Excel sheet names: ≤ 31 chars, none of : \ / ? * [ ], no leading/trailing apostrophe, not the reserved name History, unique. */
export function sheetName(n:string,used:string[]=[]):string{
  let base=n.replace(/[:\\/?*[\]]/g,' ').replace(/\s+/g,' ').trim().replace(/^'+|'+$/g,'').trim().slice(0,31).replace(/'+$/,'').trim()||'Sheet';
  if(base.toLowerCase()==='history')base='History sheet';
  let name=base,i=2;
  while(used.some(u=>u.toLowerCase()===name.toLowerCase())){const suf=' '+i++;name=base.slice(0,31-suf.length)+suf;}
  return name;
}
export const colRef=(i:number)=>{let s='';for(let n=i+1;n>0;n=Math.floor((n-1)/26))s=String.fromCharCode(65+(n-1)%26)+s;return s;};
/** Excel date serial for an ISO yyyy-mm-dd (1900 date system). */
export const dateSerial=(iso:string)=>Math.round(Date.parse(iso.slice(0,10)+'T00:00:00Z')/86400000)+25569;
// Style indexes (cellXfs): 0 text · 1 bold header · 2 ₹ money · 3 date · 4 integer · 5 percent (0.0%)
const S={text:0,head:1,money:2,date:3,int:4,pct:5};

/** Number text Excel accepts: finite only (NaN / Infinity would make the workbook "corrupt" — written as an empty cell), ≤ 15 significant digits. */
const num=(n:number)=>Number.isFinite(n)?String(Number.isInteger(n)?n:+n.toPrecision(15)):undefined;
function cellXml(c:XCell,ref:string,head:boolean):string{
  if(c===null||c===undefined||c==='')return head?`<c r="${ref}" s="${S.head}"/>`:'';
  const v=(n:number,s:number)=>{const t=num(n);return t===undefined?'':`<c r="${ref}" s="${s}"><v>${t}</v></c>`;};
  if(typeof c==='number')return v(c,Number.isInteger(c)?S.int:S.text);
  if(typeof c==='string')return `<c r="${ref}" s="${head?S.head:S.text}" t="inlineStr"><is><t xml:space="preserve">${esc(c)}</t></is></c>`;
  if('paise' in c)return v(c.paise/100,S.money);
  if('int' in c)return v(c.int,S.int);
  if('pct' in c)return v(c.pct,S.pct);
  return v(dateSerial(c.date),S.date);
}
/** Rows per encoded chunk: a full-FY workbook is never held as one huge string (W05 review F6). */
const ROW_CHUNK=2000;
function sheetXml(sh:XSheet,enc:TextEncoder):Uint8Array[]{
  const head=sh.header!==false;
  const cols=sh.widths?.length?`<cols>${sh.widths.map((w,i)=>`<col min="${i+1}" max="${i+1}" width="${w}" customWidth="1"/>`).join('')}</cols>`:'';
  const pane=head?'<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>':'';
  const out=[enc.encode(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">${pane}${cols}<sheetData>`)];
  for(let i=0;i<sh.rows.length;i+=ROW_CHUNK){
    let t='';
    for(let ri=i;ri<Math.min(i+ROW_CHUNK,sh.rows.length);ri++){const r=sh.rows[ri];t+=`<row r="${ri+1}">`;for(let ci=0;ci<r.length;ci++)t+=cellXml(r[ci],colRef(ci)+(ri+1),head&&ri===0);t+='</row>';}
    out.push(enc.encode(t));
  }
  out.push(enc.encode('</sheetData></worksheet>'));
  return out;
}
const STYLES=`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="3"><numFmt numFmtId="164" formatCode="&quot;₹&quot;#,##0.00"/><numFmt numFmtId="165" formatCode="yyyy\\-mm\\-dd"/><numFmt numFmtId="166" formatCode="0.0%"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="6"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="1" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>`;

// ── ZIP (method 0 = stored) ──
const CRC=(()=>{const t=new Uint32Array(256);for(let n=0;n<256;n++){let c=n;for(let k=0;k<8;k++)c=c&1?0xEDB88320^(c>>>1):c>>>1;t[n]=c>>>0;}return t;})();
const crcStep=(c:number,b:Uint8Array)=>{for(let i=0;i<b.length;i++)c=CRC[(c^b[i])&255]^(c>>>8);return c;};
export function crc32(b:Uint8Array|Uint8Array[]){let c=0xFFFFFFFF;for(const x of Array.isArray(b)?b:[b])c=crcStep(c,x);return (c^0xFFFFFFFF)>>>0;}
const u16=(n:number)=>[n&255,(n>>>8)&255],u32=(n:number)=>[n&255,(n>>>8)&255,(n>>>16)&255,(n>>>24)&255];
/** Stored ZIP written into ONE preallocated buffer (W05 review F6: the old JS number[] of every byte needed ~450 MB of heap for a 30 MB workbook).
 *  A file's data may be given as chunks. */
export function zipStored(files:{name:string;data:Uint8Array|Uint8Array[]}[]):Uint8Array{
  const enc=new TextEncoder();
  const fs=files.map(f=>{const chunks=Array.isArray(f.data)?f.data:[f.data];return {name:enc.encode(f.name),chunks,size:chunks.reduce((a,c)=>a+c.length,0),crc:crc32(chunks)};});
  const out=new Uint8Array(fs.reduce((a,f)=>a+30+46+2*f.name.length+f.size,0)+22);
  let p=0;const put=(a:ArrayLike<number>)=>{out.set(a,p);p+=a.length;};
  const offs:number[]=[];
  for(const f of fs){
    offs.push(p);
    put([0x50,0x4b,3,4,...u16(20),...u16(0x0800),...u16(0),...u16(0),...u16(0x21),...u32(f.crc),...u32(f.size),...u32(f.size),...u16(f.name.length),...u16(0)]);put(f.name);
    for(const c of f.chunks)put(c);
  }
  const cdOff=p;
  fs.forEach((f,i)=>{put([0x50,0x4b,1,2,...u16(20),...u16(20),...u16(0x0800),...u16(0),...u16(0),...u16(0x21),...u32(f.crc),...u32(f.size),...u32(f.size),...u16(f.name.length),...u16(0),...u16(0),...u16(0),...u16(0),...u32(0),...u32(offs[i])]);put(f.name);});
  put([0x50,0x4b,5,6,...u16(0),...u16(0),...u16(files.length),...u16(files.length),...u32(p-cdOff),...u32(cdOff),...u16(0)]);
  return out;
}
export const XLSX_MIME='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
/** Builds a workbook (one or more sheets) as .xlsx bytes. */
export function buildXlsx(sheets:XSheet[]):Uint8Array{
  const used:string[]=[];const named=(sheets.length?sheets:[{name:'Sheet1',rows:[]}]).map(s=>{const n=sheetName(s.name,used);used.push(n);return {...s,name:n};});
  const enc=new TextEncoder();const f=(name:string,text:string)=>({name,data:enc.encode(text)});
  const R='http://schemas.openxmlformats.org/officeDocument/2006/relationships',H='<?xml version="1.0" encoding="UTF-8" standalone="yes"?>';
  return zipStored([
    f('[Content_Types].xml',`${H}<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>${named.map((_,i)=>`<Override PartName="/xl/worksheets/sheet${i+1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`).join('')}</Types>`),
    f('_rels/.rels',`${H}<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="${R}/officeDocument" Target="xl/workbook.xml"/></Relationships>`),
    f('xl/workbook.xml',`${H}<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="${R}"><sheets>${named.map((s,i)=>`<sheet name="${esc(s.name)}" sheetId="${i+1}" r:id="rId${i+1}"/>`).join('')}</sheets></workbook>`),
    f('xl/_rels/workbook.xml.rels',`${H}<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">${named.map((_,i)=>`<Relationship Id="rId${i+1}" Type="${R}/worksheet" Target="worksheets/sheet${i+1}.xml"/>`).join('')}<Relationship Id="rId${named.length+1}" Type="${R}/styles" Target="styles.xml"/></Relationships>`),
    f('xl/styles.xml',STYLES),
    ...named.map((s,i)=>({name:`xl/worksheets/sheet${i+1}.xml`,data:sheetXml(s,enc)})),
  ]);
}
