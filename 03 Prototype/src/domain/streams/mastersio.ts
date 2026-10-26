// MA-8 · Import / export of masters (plan Part 2 §8 M30). Pure domain: the masters JSON export, the CSV text of a list (the same file the
// server import reads: 08 Production/deployment/import-templates/templates/<wire>.csv — headers = FieldSpec keys), and "paste rows"
// (CSV text → rows through the master's FieldSpec, with per-row errors). Commands: services/mock/handlers/masters.ts (MST_IMPORT).
//
//   JSON  {format:'hcc-masters',version:1,schema,exportedAt,businessDate,property,masters:{<id>:{label,rows:[{code,name,sortOrder,active,<fields>}]}},excluded:[…]}
//         rows carry no ids (ids are local); money lists only for the Owner, money fields only for roles that see money; ID numbers (a vendor's
//         PAN) and per-day toggles (sold out today) are never included — nothing secret is ever exported.
//   CSV   code,name,<field keys…>,sortOrder,active   money in rupees, percent without %, yes/no, lists separated by ';', a dated amount = the
//         amount in force today, a multi-line text field (checklist) = lines separated by ';'. A blank cell clears the value; a blank dated
//         amount keeps the amounts; blank sortOrder / active keep. A column left out of the file leaves that column as it is.
import type {State} from '../types';
import {SCHEMA} from '../types';
import {masterSpecs,mstSpec,mstRows,mstMoneyOn,mstCanEdit,mstVisibleRow,type MasterSpec,type MasterRow,type FieldSpec,type MstDatedAmount} from './masters';

export const MST_IO_FORMAT='hcc-masters';
export const MST_IO_BASE=['code','name','sortOrder','active'] as const;
/** Fields that are never exported or imported: ID numbers (DEC-068) and per-day toggles. */
export const MST_IO_SKIP:Record<string,string[]>={expenseVendors:['pan'],menuItems:['soldOutToday']};
/** Server import file (wire) of each master — 08 Production ImportLists / pos-outlets / pos-menu. roomTypes and rooms keep their older rooms import. */
export const MST_IO_WIRE:Record<string,string>={
  floors:'floors',amenities:'amenities',occupancyCharges:'occupancy-charges',mealPlans:'meal-plans',extras:'extras',discountReasons:'discount-reasons',
  paymentMethods:'payment-methods',bankAccounts:'bank-accounts',expenseCategories:'expense-categories',expenseVendors:'expense-vendors',
  outlets:'pos-outlets',posTables:'pos-tables',menuCategories:'menu-categories',menuItems:'pos-menu',bookingSources:'booking-sources',idTypes:'id-types',
  numberSeries:'number-series',policies:'policies',designations:'designations',shiftPatterns:'shift-patterns',leaveTypes:'leave-types',holidays:'holidays',
  hkTaskTypes:'hk-task-types',hkZones:'hk-zones',complaintCategories:'complaint-categories',lostFoundCategories:'lost-found-categories'};
/** A blank toggle cell: the default of the field (everything else is off). */
export const MST_IO_TOGGLE_DEFAULT:Record<string,boolean>={showOnWebsite:true,accepted:true};
const TOGGLE_DEFAULT=MST_IO_TOGGLE_DEFAULT;

export type MstIoRow=Record<string,unknown>;
/** One list's rows to import; source 'csv' = pasted text (a dated field carries only today's amount). */
export type MstIoSet={master:string;rows:MstIoRow[];source?:'csv'|'json'};

/** Fields of a master that travel in files (spec order, without code / name / sortOrder / active and the skipped ones). */
export const mstIoFields=(spec:MasterSpec):FieldSpec[]=>spec.fields.filter(f=>!(MST_IO_BASE as readonly string[]).includes(f.key)&&!(MST_IO_SKIP[spec.id]??[]).includes(f.key));
/** CSV headers of a master: code, name, its fields in order, sortOrder, active. */
export const mstCsvColumns=(spec:MasterSpec):string[]=>['code','name',...mstIoFields(spec).map(f=>f.key),'sortOrder','active'];
const norm=(x:string)=>x.toLowerCase().replace(/[^a-z0-9]/g,'');
const lines=(v:unknown)=>String(v??'').split(/\r?\n/).map(x=>x.trim()).filter(Boolean);

// ───────────────────────────── JSON export / import ─────────────────────────────
export type MstExport={format:string;version:number;schema:number;exportedAt:string;businessDate:string;property:string;
  masters:Record<string,{label:string;rows:MstIoRow[]}>;excluded:string[]};
/** One row as it travels (no id / retiredOn; money fields only when the user sees money; skipped fields removed). */
export function mstExportRow(s:State,uid:string,spec:MasterSpec,row:MasterRow):MstIoRow{
  const v=mstVisibleRow(s,uid,spec,row),out:MstIoRow={code:v.code,name:v.name,sortOrder:v.sortOrder,active:v.active};
  for(const f of mstIoFields(spec))if(v[f.key]!==undefined)out[f.key]=JSON.parse(JSON.stringify(v[f.key]));
  return out;
}
/** Masters export (Master configuration › Data and system). A list the user may not change is left out and named in `excluded`. */
export function mstExport(s:State,uid:string):MstExport{
  const masters:MstExport['masters']={},excluded:string[]=[];
  for(const spec of masterSpecs()){
    const d=mstCanEdit(s,uid,spec);
    if(!d.ok){excluded.push(`${spec.id} — ${d.reason}`);continue;}
    masters[spec.id]={label:spec.label,rows:mstRows(s,spec.id).map(r=>mstExportRow(s,uid,spec,r))};
    for(const k of MST_IO_SKIP[spec.id]??[])excluded.push(`${spec.id}.${k} — never exported`);
  }
  return {format:MST_IO_FORMAT,version:1,schema:SCHEMA,exportedAt:s.clock,businessDate:s.date,property:s.settings.hotel,masters,excluded};
}
/** Masters in dependency order (a list that picks from another master comes after it). */
export function mstImportOrder(ids:string[]):string[]{
  const out:string[]=[],seen=new Set<string>();
  const visit=(id:string)=>{if(seen.has(id)||!ids.includes(id))return;seen.add(id);const sp=mstSpec(id);
    for(const f of sp?.fields??[])if(f.master)visit(f.master);out.push(id);};
  for(const id of ids)visit(id);
  return out;
}
/** Sets of a masters export file (or an error); lists not in this registry are named in `skipped`. */
export function mstSetsFromExport(data:unknown):{sets:MstIoSet[];errors:string[];skipped:{master:string;reason:string}[]}{
  const d=data as Partial<MstExport>|null;
  if(!d||typeof d!=='object'||d.format!==MST_IO_FORMAT||typeof d.masters!=='object'||!d.masters)return {sets:[],skipped:[],errors:['This is not a masters export file (Master configuration › Data and system › Export masters).']};
  if(d.version!==1)return {sets:[],skipped:[],errors:[`Unsupported export version ${String(d.version)}.`]};
  const sets:MstIoSet[]=[],skipped:{master:string;reason:string}[]=[];
  for(const [id,m] of Object.entries(d.masters)){
    if(!mstSpec(id)){skipped.push({master:id,reason:'Not in this registry'});continue;}
    if(!m||!Array.isArray((m as {rows?:unknown}).rows)){skipped.push({master:id,reason:'No rows'});continue;}
    sets.push({master:id,rows:(m as {rows:MstIoRow[]}).rows});
  }
  return {sets:mstImportOrder(sets.map(x=>x.master)).map(id=>sets.find(x=>x.master===id)!),errors:[],skipped};
}
/** Amounts of a dated field after an import row (old = the stored list or none for a new row). Amounts before today stay as they are (an amount in
 *  effect never changes); the amount in force today is the file's, a different one starting today; later (scheduled) amounts are the file's, or —
 *  for a CSV file, which carries only today's amount — the stored ones. A file with no amount keeps the stored list. */
export function mstMergeDated(old:unknown,incoming:unknown,today:string,keepFuture=false):MstDatedAmount[]{
  const o=(Array.isArray(old)?old as MstDatedAmount[]:[]),n=(Array.isArray(incoming)?incoming as MstDatedAmount[]:[]);
  if(!n.length)return o;
  const now=mstMoneyOn(n,today);
  const future=(keepFuture?o:n).filter(x=>x.effectiveFrom>today).map(x=>({amount:x.amount,effectiveFrom:x.effectiveFrom}));
  const sameFuture=JSON.stringify(o.filter(x=>x.effectiveFrom>today))===JSON.stringify(future);
  if(now===mstMoneyOn(o,today)&&sameFuture)return o; // nothing changes: the stored list stays exactly as it is
  const next=o.filter(x=>x.effectiveFrom<today).map(x=>({...x}));
  if(now!==undefined&&now!==mstMoneyOn(next,today))next.push({amount:now,effectiveFrom:today});
  next.push(...future);
  return next.sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
}

// ───────────────────────────── CSV ─────────────────────────────
/** RFC 4180 reader: quoted cells with doubled quotes and line breaks; the delimiter of the header line (comma, else semicolon, else tab). */
export function mstParseCsv(text:string):{headers:string[];rows:{line:number;cells:string[]}[];error?:string}{
  let s=text??'';if(s.charCodeAt(0)===0xFEFF)s=s.slice(1);
  if(!s.trim())return {headers:[],rows:[],error:'Paste the rows first (the first line is the column names).'};
  const head=s.split(/\r?\n/,1)[0];const delim=head.includes(',')?',':head.includes(';')?';':head.includes('\t')?'\t':',';
  const recs:{line:number;cells:string[]}[]=[];let cells:string[]=[],cell='',q=false,started=false,line=1,recLine=1;
  for(let i=0;i<s.length;i++){
    const c=s[i];
    if(q){if(c==='"'){if(s[i+1]==='"'){cell+='"';i++;}else q=false;}else{if(c==='\n')line++;cell+=c;}continue;}
    if(c==='"'&&!started){q=true;started=true;}
    else if(c===delim){cells.push(cell);cell='';started=false;}
    else if(c==='\r'||c==='\n'){if(c==='\r'&&s[i+1]==='\n')i++;cells.push(cell);cell='';started=false;recs.push({line:recLine,cells});cells=[];line++;recLine=line;}
    else{cell+=c;started=true;}
  }
  if(q)return {headers:[],rows:[],error:`A quoted cell starting on line ${recLine} is never closed (missing ").`};
  if(cell||cells.length){cells.push(cell);recs.push({line:recLine,cells});}
  const kept=recs.filter(r=>r.cells.some(x=>x.trim()));
  if(!kept.length)return {headers:[],rows:[],error:'Paste the rows first (the first line is the column names).'};
  return {headers:kept[0].cells.map(x=>x.trim()),rows:kept.slice(1)};
}
export const mstCsvCell=(v:string)=>/[",\r\n]/.test(v)?'"'+v.replace(/"/g,'""')+'"':v;
const rupees=(paise:number)=>(paise/100).toFixed(2);
const percentText=(bps:number)=>String(Number((bps/100).toFixed(2)));
/** A field value as its CSV cell. */
export function mstCellOf(s:State,spec:MasterSpec,f:FieldSpec,v:unknown):string{
  if(v===undefined||v===null)return '';
  switch(f.type){
    case 'toggle':return v?'yes':'no';
    case 'money':return rupees(Number(v));
    case 'percent':return percentText(Number(v));
    case 'datedMoney':{const a=mstMoneyOn(v,s.date);return a===undefined?'':rupees(a);}
    case 'multiselect':return (Array.isArray(v)?v:[]).map(x=>labelOf(s,f,String(x),true)).join(';');
    case 'select':return labelOf(s,f,String(v),true);
    case 'text':return f.multiline?lines(v).join(';'):String(v);
    default:return String(v);
  }
}
/** Option value ↔ shown text of a select: fixed options give values (the file carries values); optionsFrom choices carry the label (a person's name). */
function labelOf(s:State,f:FieldSpec,v:string,toFile:boolean):string{
  if(f.optionsFrom&&toFile)return f.optionsFrom(s).find(o=>o.value===v)?.label??v;
  return v;
}
/** The rows of a master as CSV text (headers = mstCsvColumns). */
export function mstToCsv(s:State,spec:MasterSpec,rows:MasterRow[]=mstRows(s,spec.id)):string{
  const cols=mstCsvColumns(spec);
  const out=[cols.join(',')];
  for(const r of rows){
    out.push(cols.map(c=>{
      if(c==='active')return r.active?'yes':'no';
      if(c==='sortOrder')return String(r.sortOrder);
      if(c==='code'||c==='name')return mstCsvCell(String(r[c]??''));
      const f=spec.fields.find(x=>x.key===c)!;return mstCsvCell(mstCellOf(s,spec,f,r[c]));
    }).join(','));
  }
  return out.join('\r\n')+'\r\n';
}
export type MstCsvIssue={line:number;column:string;message:string};
export type MstCsvResult={rows:{line:number;row:MstIoRow}[];errors:MstCsvIssue[];ignored:string[];columns:string[]};
/** Parse a cell into the value of the field; undefined = leave the field out (keep what is there). */
function valueOf(s:State,f:FieldSpec,cell:string,issue:(m:string)=>void):unknown{
  const t=cell.trim();
  switch(f.type){
    case 'toggle':{if(!t)return TOGGLE_DEFAULT[f.key]??false;const x=t.toLowerCase();if(['yes','y','true','1','on'].includes(x))return true;if(['no','n','false','0','off'].includes(x))return false;issue(`${f.label}: enter yes or no.`);return undefined;}
    case 'number':{if(!t)return '';const n=Number(t.replace(/,/g,''));if(!Number.isFinite(n)){issue(`${f.label}: enter a number.`);return undefined;}return n;}
    case 'money':{if(!t)return '';const n=Number(t.replace(/[₹,\s]|rs\.?/gi,''));if(!Number.isFinite(n)||n<0){issue(`${f.label}: enter an amount in rupees (0 or more).`);return undefined;}return Math.round(n*100);}
    case 'percent':{if(!t)return '';const n=Number(t.replace(/%/g,'').trim());if(!Number.isFinite(n)){issue(`${f.label}: enter a percentage such as 12.5.`);return undefined;}return Math.round(n*100);}
    case 'datedMoney':{if(!t)return undefined;const n=Number(t.replace(/[₹,\s]|rs\.?/gi,''));if(!Number.isFinite(n)||n<0){issue(`${f.label}: enter an amount in rupees (0 or more).`);return undefined;}return [{amount:Math.round(n*100),effectiveFrom:s.date}] as MstDatedAmount[];}
    case 'select':{if(!t)return '';return pick(s,f,t,issue);}
    case 'multiselect':{const items=t.split(/[;|]/).map(x=>x.trim()).filter(Boolean);const out:string[]=[];for(const x of items){const p=pick(s,f,x,issue);if(p===undefined)return undefined;if(!out.includes(String(p)))out.push(String(p));}return out;}
    default:return f.multiline?t.split(';').map(x=>x.trim()).filter(Boolean).join('\n'):t;
  }
}
/** A select / multiselect item by value or label (case-insensitive): fixed options, another master's code or name, or the computed choices. */
function pick(s:State,f:FieldSpec,text:string,issue:(m:string)=>void):unknown{
  const low=text.toLowerCase();
  const opts=f.options??(f.optionsFrom?f.optionsFrom(s):undefined);
  if(opts){const o=opts.find(x=>x.value.toLowerCase()===low)??opts.find(x=>x.label.toLowerCase()===low);if(o)return o.value;issue(`${f.label}: “${text}” is not one of ${opts.slice(0,8).map(x=>x.value).join(', ')}${opts.length>8?' …':''}.`);return undefined;}
  if(f.master){const r=mstRows(s,f.master).find(x=>x.code.toLowerCase()===low)??mstRows(s,f.master).find(x=>x.name.toLowerCase()===low);if(r)return r.code;issue(`${f.label}: “${text}” is not in ${mstSpec(f.master)?.label??f.master}.`);return undefined;}
  return text;
}
/** CSV text → rows of a master (through its FieldSpecs) with per-row errors; nothing is changed here. */
export function mstRowsFromCsv(s:State,spec:MasterSpec,text:string):MstCsvResult{
  const res:MstCsvResult={rows:[],errors:[],ignored:[],columns:[]};
  const p=mstParseCsv(text);
  if(p.error){res.errors.push({line:0,column:'',message:p.error});return res;}
  const fields=mstIoFields(spec),byKey=new Map<string,string>();
  for(const c of mstCsvColumns(spec))byKey.set(norm(c),c);
  const cols:(string|undefined)[]=[];
  p.headers.forEach(h=>{
    const k=byKey.get(norm(h));
    if(!k){
      if((MST_IO_SKIP[spec.id]??[]).some(x=>norm(x)===norm(h))&&spec.id==='expenseVendors')res.errors.push({line:1,column:h,message:`Column “${h}” is not allowed: files never carry ID numbers (type a PAN on the vendor itself).`});
      else if(norm(h))res.ignored.push(h);
      cols.push(undefined);return;
    }
    if(cols.includes(k))res.errors.push({line:1,column:h,message:`Column “${h}” appears twice.`});
    cols.push(k);
  });
  res.columns=cols.filter(Boolean) as string[];
  for(const need of ['code','name',...fields.filter(f=>f.required&&!['code','name'].includes(f.key)).map(f=>f.key)])
    if(!cols.includes(need))res.errors.push({line:1,column:need,message:`Required column “${need}” is missing. Use the headers of Copy CSV.`});
  if(res.errors.length)return res;
  if(!p.rows.length){res.errors.push({line:0,column:'',message:'The text has a header line but no rows.'});return res;}
  for(const r of p.rows){
    const row:MstIoRow={};let bad=false;
    const issue=(column:string)=>(message:string)=>{res.errors.push({line:r.line,column,message});bad=true;};
    cols.forEach((k,i)=>{
      if(!k)return;const cell=r.cells[i]??'';
      if(k==='code')row.code=cell.trim().toUpperCase();
      else if(k==='name')row.name=cell.trim();
      else if(k==='sortOrder'){const t=cell.trim();if(t){const n=Number(t);if(!Number.isInteger(n)||n<0||n>1_000_000)issue(k)('Sort order: a whole number from 0 to 1,000,000.');else row.sortOrder=n;}}
      else if(k==='active'){const t=cell.trim().toLowerCase();if(t){if(['yes','y','true','1'].includes(t))row.active=true;else if(['no','n','false','0'].includes(t))row.active=false;else issue(k)('Active: enter yes or no.');}}
      else{const f=fields.find(x=>x.key===k)!;const v=valueOf(s,f,cell,issue(k));if(v!==undefined)row[k]=v;}
    });
    if(!bad)res.rows.push({line:r.line,row});
  }
  return res;
}

// ───────────────────────────── Import report (shared by the handler, the preview and the sheets) ─────────────────────────────
export type MstImportItem={master:string;code:string;name:string;action:'create'|'update'|'unchanged'|'skipped'|'error';detail:string;line?:number};
export type MstImportReport={items:MstImportItem[];counts:{create:number;update:number;unchanged:number;skipped:number;errors:number};skippedMasters:{master:string;reason:string}[]};
export const mstImportCounts=(items:MstImportItem[])=>({create:items.filter(i=>i.action==='create').length,update:items.filter(i=>i.action==='update').length,
  unchanged:items.filter(i=>i.action==='unchanged').length,skipped:items.filter(i=>i.action==='skipped').length,errors:items.filter(i=>i.action==='error').length});
/** Row keys a file may set, for a spec (everything else is dropped). */
export const mstIoKeys=(spec:MasterSpec)=>new Set<string>(['code','name','sortOrder','active',...mstIoFields(spec).map(f=>f.key)]);
