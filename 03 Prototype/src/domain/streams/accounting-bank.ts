// ACC-4 · Bank reconciliation and UPI / card clearing (02 Planning/agents/ACC4-CONTRACT.md §2; rulebook B8, T14a / T14b; owner default A23).
// Pure helpers: statement layouts (mapping master), CSV parsing to rows, import checks, matching rules, cleared dates, the B8 settlement
// journal, the clearing view and the reconciliation report. Money = integer paise; bank view: credit = money in. Slice s.accBankX (optional).
import type {State} from '../types';
import {accVouchersUpTo,type ACCVoucher} from './accounting';
import {accRptIsOpening} from './accounting-reports';
import {accFyOfDate,accYearLocked} from './accounting-year';
import {ACC_BANK_LAYOUT_SEED} from '../../fixtures/ext/bank-layouts';

export type AccBankDateFormat='DD/MM/YYYY'|'DD-MM-YYYY'|'YYYY-MM-DD'|'DD-MMM-YYYY';
export interface BankStatementLayout{id:string;name:string;bankLedger:string;delimiter:','|';'|'\t';headerRows:number;dateFormat:AccBankDateFormat;
  columns:{date:string;narration:string;ref?:string;debit?:string;credit?:string;amount?:string;drCrFlag?:string;balance?:string};
  amountSign?:'debit-negative';active:boolean;
  /** Regex (source) finding a UTR / cheque number in the narration when no ref column is mapped (rule 1). */ refPattern?:string;
  /** Regex (source) of a UPI / card settlement credit (rule 3, B8). */ settlementPattern?:string}
export interface BankStatementImport{id:string;bankLedger:string;layoutId:string;fileName:string;fileHash:string;from:string;to:string;
  openingBalance:number;closingBalance:number;rows:number;importedBy:string;importedAt:string}
export interface BankStatementRow{id:string;importId:string;bankLedger:string;date:string;narration:string;ref?:string;debit:number;credit:number;balance?:number;
  status:'unmatched'|'matched'|'ignored';matchId?:string}
export type AccBankLineRef={voucherId:string;line:number};
export interface BankMatch{id:string;rowIds:string[];voucherLineRefs:AccBankLineRef[];rule:'auto-ref'|'auto-amount-date'|'manual'|'created';clearedDate:string;by:string;at:string;undoneAt?:string}
export type AccBankSlice={layouts?:BankStatementLayout[];imports:BankStatementImport[];rows:BankStatementRow[];matches:BankMatch[]};

export const ACC_BANK_REF_DAYS=7,ACC_BANK_AFTER_DAYS=3;
export const accBankSlice=(s:State):AccBankSlice=>s.accBankX??{imports:[],rows:[],matches:[]};
/** Layouts = the seed (Generic, A23) overlaid by the Owner's rows (same id replaces). */
export function accBankLayouts(s:State):BankStatementLayout[]{
  const own=accBankSlice(s).layouts??[];return [...ACC_BANK_LAYOUT_SEED.filter(l=>!own.some(o=>o.id===l.id)),...own];
}
export const accBankLayout=(s:State,id:string)=>accBankLayouts(s).find(l=>l.id===id);

// ── Parsing ──
const MON:Record<string,string>={JAN:'01',FEB:'02',MAR:'03',APR:'04',MAY:'05',JUN:'06',JUL:'07',AUG:'08',SEP:'09',OCT:'10',NOV:'11',DEC:'12'};
export function accBankParseDate(v:string,f:AccBankDateFormat):string|undefined{
  const t=v.trim();let m:RegExpExecArray|null;let y='',mo='',d='';
  if(f==='YYYY-MM-DD'&&(m=/^(\d{4})-(\d{2})-(\d{2})$/.exec(t)))[y,mo,d]=[m[1],m[2],m[3]];
  else if(f==='DD/MM/YYYY'&&(m=/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(t)))[d,mo,y]=[m[1],m[2],m[3]];
  else if(f==='DD-MM-YYYY'&&(m=/^(\d{1,2})-(\d{1,2})-(\d{4})$/.exec(t)))[d,mo,y]=[m[1],m[2],m[3]];
  else if(f==='DD-MMM-YYYY'&&(m=/^(\d{1,2})-([A-Za-z]{3})-(\d{4})$/.exec(t))&&MON[m[2].toUpperCase()])[d,mo,y]=[m[1],MON[m[2].toUpperCase()],m[3]];
  else return undefined;
  const iso=`${y}-${mo.padStart(2,'0')}-${d.padStart(2,'0')}`,dt=new Date(iso+'T00:00:00Z');
  return !isNaN(+dt)&&dt.toISOString().slice(0,10)===iso?iso:undefined;
}
/** "1,48,230.00" / "-590" / "" → paise (undefined when not a number). */
export function accBankParseAmount(v:string|undefined):number|undefined{
  const t=String(v??'').replace(/[,\s₹]/g,'').replace(/^\((.*)\)$/,'-$1');if(!t)return 0;
  if(!/^-?\d+(\.\d{1,2})?$/.test(t))return undefined;
  const neg=t.startsWith('-'),[a,b='']=t.replace('-','').split('.');return (neg?-1:1)*(+a*100+ +(b+'00').slice(0,2));
}
/** Splits delimited text (quotes honoured). */
export function accBankSplit(text:string,delim:string):string[][]{
  const out:string[][]=[];let row:string[]=[],cell='',q=false;
  for(let i=0;i<text.length;i++){const ch=text[i];
    if(q){if(ch==='"'){if(text[i+1]==='"'){cell+='"';i++;}else q=false;}else cell+=ch;continue;}
    if(ch==='"')q=true;else if(ch===delim){row.push(cell);cell='';}else if(ch==='\n'||ch==='\r'){if(ch==='\r'&&text[i+1]==='\n')i++;row.push(cell);cell='';if(row.some(c=>c.trim()))out.push(row);row=[];}else cell+=ch;}
  row.push(cell);if(row.some(c=>c.trim()))out.push(row);return out;
}
/** Wave D CSV formula guard: a cell Excel would run as a formula is kept as text (leading apostrophe). */
const guard=(v:string)=>/^[=+\-@\t\r]/.test(v)&&!/^-?\d/.test(v)?"'"+v:v;
/** FNV-1a 64-bit as hex (prototype file hash; the server uses SHA-256). */
export function accBankHash(text:string):string{
  let h=0xcbf29ce484222325n;const p=0x100000001b3n,m=(1n<<64n)-1n;
  for(let i=0;i<text.length;i++){h^=BigInt(text.charCodeAt(i));h=(h*p)&m;}return h.toString(16).padStart(16,'0');
}
export type AccBankParsedRow={line:number;date:string;narration:string;ref?:string;debit:number;credit:number;balance?:number};
export type AccBankParse={rows:AccBankParsedRow[];error?:{code:'BANK_LAYOUT'|'BANK_PARSE'|'BANK_BALANCE_BREAK';message:string;line?:number};openingBalance:number;closingBalance:number};
export function accBankParse(text:string,layout:BankStatementLayout):AccBankParse{
  const all=accBankSplit(text.replace(/^﻿/,''),layout.delimiter);
  const fail=(code:'BANK_LAYOUT'|'BANK_PARSE'|'BANK_BALANCE_BREAK',message:string,line?:number):AccBankParse=>({rows:[],error:{code,message,line},openingBalance:0,closingBalance:0});
  if(all.length<layout.headerRows)return fail('BANK_LAYOUT','The file has no header row.');
  const head=(all[layout.headerRows-1]??[]).map(h=>h.trim().toLowerCase());
  const col=(name?:string)=>name?head.indexOf(name.trim().toLowerCase()):-1;
  const c=layout.columns,ix={date:col(c.date),narr:col(c.narration),ref:col(c.ref),dr:col(c.debit),cr:col(c.credit),amt:col(c.amount),flag:col(c.drCrFlag),bal:col(c.balance)};
  if(ix.date<0||ix.narr<0)return fail('BANK_LAYOUT',`Columns "${c.date}" and "${c.narration}" must be in the header row.`);
  if(!((ix.dr>=0&&ix.cr>=0)||ix.amt>=0))return fail('BANK_LAYOUT','Map either debit and credit columns or one amount column.');
  const rows:AccBankParsedRow[]=[];
  for(let i=layout.headerRows;i<all.length;i++){
    const r=all[i],line=i+1,get=(k:number)=>k>=0?String(r[k]??'').trim():'';
    const date=accBankParseDate(get(ix.date),layout.dateFormat);if(!date)return fail('BANK_PARSE',`Row ${line}: "${get(ix.date)}" is not a ${layout.dateFormat} date.`,line);
    let debit=0,credit=0;
    if(ix.amt>=0){const a=accBankParseAmount(get(ix.amt));if(a===undefined)return fail('BANK_PARSE',`Row ${line}: amount "${get(ix.amt)}" is not a number.`,line);
      const flag=get(ix.flag).toUpperCase();
      if(flag)(flag.startsWith('D')?(debit=Math.abs(a)):(credit=Math.abs(a)));else if(a<0)debit=-a;else credit=a;}
    else{const d=accBankParseAmount(get(ix.dr)),cr=accBankParseAmount(get(ix.cr));if(d===undefined||cr===undefined)return fail('BANK_PARSE',`Row ${line}: debit / credit is not a number.`,line);debit=Math.abs(d);credit=Math.abs(cr);}
    if(debit&&credit)return fail('BANK_PARSE',`Row ${line}: a row carries either a debit or a credit.`,line);
    const balRaw=get(ix.bal),balance=ix.bal>=0&&balRaw?accBankParseAmount(balRaw):undefined;
    if(ix.bal>=0&&balRaw&&balance===undefined)return fail('BANK_PARSE',`Row ${line}: balance is not a number.`,line);
    let ref=get(ix.ref)||undefined;const narration=guard(get(ix.narr));
    if(!ref&&layout.refPattern){const m=new RegExp(layout.refPattern).exec(narration);if(m)ref=m[1]??m[0];}
    rows.push({line,date,narration,...(ref?{ref:guard(ref)}:{}),debit,credit,...(balance!==undefined?{balance}:{})});
  }
  if(!rows.length)return fail('BANK_PARSE','The file has no statement rows.');
  let opening=0,closing=0;
  if(rows.every(r=>r.balance!==undefined)){
    opening=rows[0].balance!-rows[0].credit+rows[0].debit;let run=opening;
    for(const r of rows){run+=r.credit-r.debit;if(run!==r.balance)return fail('BANK_BALANCE_BREAK',`BANK_BALANCE_BREAK: the running balance breaks at row ${r.line} (expected ₹${(run/100).toFixed(2)}, the file says ₹${(r.balance!/100).toFixed(2)}).`,r.line);}
    closing=run;
  }else closing=rows.reduce((a,r)=>a+r.credit-r.debit,0);
  return {rows,openingBalance:opening,closingBalance:closing};
}
/** Import refusals beyond parsing: duplicate file, a row outside an open FY; and the rows overlapping an earlier import (flagged). */
export function accBankImportPlan(s:State,ledger:string,text:string,layout:BankStatementLayout):AccBankParse&{hash:string;duplicateFile?:boolean;lockedRow?:AccBankParsedRow;overlaps:number[]}{
  const p=accBankParse(text,layout),hash=accBankHash(text),x=accBankSlice(s);
  const duplicateFile=x.imports.some(i=>i.fileHash===hash);
  const lockedRow=p.rows.find(r=>accYearLocked(s,accFyOfDate(r.date)));
  const key=(r:{date:string;debit:number;credit:number;ref?:string;narration:string})=>[r.date,r.credit-r.debit,r.ref??'',r.narration].join('|');
  const prev=new Set(x.rows.filter(r=>r.bankLedger===ledger).map(key));
  return {...p,hash,...(duplicateFile?{duplicateFile}:{}),...(lockedRow?{lockedRow}:{}),overlaps:p.rows.filter(r=>prev.has(key(r))).map(r=>r.line)};
}

// ── Book lines on a bank ledger ──
export type AccBankBookLine={ref:AccBankLineRef;no:string;date:string;narration:string;amount:number;reference?:string}; // amount = Dr − Cr (money in > 0)
export const accBankVoucherId=(v:ACCVoucher)=>`${v.source.kind}:${v.source.id}:${v.source.sub}`;
const refKey=(r:AccBankLineRef)=>r.voucherId+'#'+r.line;
export function accBankBookLines(s:State,ledger:string,to:string):AccBankBookLine[]{
  const out:AccBankBookLine[]=[];
  for(const v of accVouchersUpTo(s,to)){if(accRptIsOpening(v))continue;
    v.entries.forEach((e,i)=>{if(e.account===ledger&&(e.debit||e.credit))out.push({ref:{voucherId:accBankVoucherId(v),line:i},no:v.no,date:v.date,narration:v.narration,amount:e.debit-e.credit});});}
  return out;
}
const live=(m:BankMatch)=>!m.undoneAt;
export function accBankMatchedRefs(s:State):Map<string,BankMatch>{
  const m=new Map<string,BankMatch>();for(const x of accBankSlice(s).matches.filter(live))for(const r of x.voucherLineRefs)m.set(refKey(r),x);return m;
}
/** Cleared date of a book line (= the statement row date of its match), or undefined. */
export const accBankClearedDate=(s:State,r:AccBankLineRef)=>accBankMatchedRefs(s).get(refKey(r))?.clearedDate;
const days=(a:string,b:string)=>Math.round((Date.parse(b+'T00:00:00Z')-Date.parse(a+'T00:00:00Z'))/86400000);
const rowAmt=(r:{debit:number;credit:number})=>r.credit-r.debit;

// ── Matching rules (§2.3) ──
export type AccBankProposal={rowIds:string[];lines:AccBankBookLine[];rule:'auto-ref'|'auto-amount-date';clearedDate:string};
export type AccBankAuto={proposals:AccBankProposal[];settlements:string[];ambiguous:string[]};
/** Rules 1–3 over the unmatched rows of `ledger` (never across a locked year). Pure: the handler records the proposals. */
export function accBankAutoMatch(s:State,ledger:string):AccBankAuto{
  const x=accBankSlice(s),layouts=accBankLayouts(s);
  const rows=x.rows.filter(r=>r.bankLedger===ledger&&r.status==='unmatched'&&!accYearLocked(s,accFyOfDate(r.date))).sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:0);
  if(!rows.length)return {proposals:[],settlements:[],ambiguous:[]};
  const to=rows.at(-1)!.date,used=new Set(accBankMatchedRefs(s).keys());
  const book=accBankBookLines(s,ledger,to).filter(l=>!accYearLocked(s,accFyOfDate(l.date)));
  const free=()=>book.filter(l=>!used.has(refKey(l.ref)));
  const proposals:AccBankProposal[]=[],settlements:string[]=[],ambiguous:string[]=[];
  const imp=new Map(x.imports.map(i=>[i.id,i]));
  for(const r of rows){
    const amt=rowAmt(r);
    if(r.ref){const c=free().filter(l=>l.amount===amt&&Math.abs(days(l.date,r.date))<=ACC_BANK_REF_DAYS&&l.narration.includes(r.ref!));
      if(c.length===1){used.add(refKey(c[0].ref));proposals.push({rowIds:[r.id],lines:[c[0]],rule:'auto-ref',clearedDate:r.date});continue;}}
    const c=free().filter(l=>l.amount===amt&&days(l.date,r.date)>=0&&days(l.date,r.date)<=ACC_BANK_AFTER_DAYS);
    if(c.length===1){used.add(refKey(c[0].ref));proposals.push({rowIds:[r.id],lines:[c[0]],rule:'auto-amount-date',clearedDate:r.date});continue;}
    if(c.length>1){ambiguous.push(r.id);continue;}
    const lay=layouts.find(l=>l.id===imp.get(r.importId)?.layoutId);
    if(r.credit&&lay?.settlementPattern&&new RegExp(lay.settlementPattern,'i').test(r.narration))settlements.push(r.id);
  }
  return {proposals,settlements,ambiguous};
}
/** Rule 4 (manual): many-to-one / one-to-many, sums equal; '' = allowed, else the refusal. */
export function accBankManualProblem(s:State,rowIds:string[],refs:AccBankLineRef[]):string{
  const x=accBankSlice(s),rows=rowIds.map(id=>x.rows.find(r=>r.id===id));
  if(!rows.length||!refs.length)return 'Choose at least one statement row and one book line.';
  if(rows.some(r=>!r))return 'A statement row was not found.';
  if(rows.some(r=>r!.status!=='unmatched'))return 'A statement row is already matched or ignored.';
  const ledger=rows[0]!.bankLedger;if(rows.some(r=>r!.bankLedger!==ledger))return 'All rows must be on one bank ledger.';
  if(rows.some(r=>accYearLocked(s,accFyOfDate(r!.date))))return 'ACC_YEAR_LOCKED: a row is dated in a locked year.';
  const to=rows.map(r=>r!.date).sort().at(-1)!;const book=accBankBookLines(s,ledger,to),used=accBankMatchedRefs(s);
  let sum=0;for(const ref of refs){const l=book.find(b=>refKey(b.ref)===refKey(ref));if(!l)return `Book line ${refKey(ref)} is not on ${ledger}.`;if(used.has(refKey(ref)))return `Book line ${l.no} is already matched.`;sum+=l.amount;}
  const rs=rows.reduce((a,r)=>a+rowAmt(r!),0);
  return rs===sum?'':`BANK_MATCH_SUM: the rows total ₹${(rs/100).toFixed(2)} and the book lines ₹${(sum/100).toFixed(2)} — they must be equal.`;
}

// ── B8 settlement journal (§2.5) ──
export type AccBankSettlementInput={bankLedger:string;net:number;clearing:'1010'|'1020';gross:number;mdr:number;mdrGst:number;itc:boolean};
/** Dr bank net · Dr 5080 MDR (+ its GST unless ITC, then Dr 1310 / 1320 halves) · Cr clearing gross. Refusal text or the lines. */
export function accBankSettlementLines(x:AccBankSettlementInput):{lines:{account:string;debit:number;credit:number}[]}|{error:string}{
  for(const v of [x.gross,x.mdr,x.mdrGst,x.net])if(!Number.isInteger(v)||v<0)return {error:'Amounts are whole paise, zero or more.'};
  if(x.gross-x.mdr-x.mdrGst!==x.net)return {error:`BANK_SETTLEMENT_SUM: gross ₹${(x.gross/100).toFixed(2)} − MDR ₹${(x.mdr/100).toFixed(2)} − GST ₹${(x.mdrGst/100).toFixed(2)} must equal the credit ₹${(x.net/100).toFixed(2)}.`};
  const lines=[{account:x.bankLedger,debit:x.net,credit:0}];
  const charge=x.mdr+(x.itc?0:x.mdrGst);if(charge)lines.push({account:'5080',debit:charge,credit:0});
  if(x.itc&&x.mdrGst){const c=Math.trunc(x.mdrGst/2);lines.push({account:'1310',debit:x.mdrGst-c,credit:0},{account:'1320',debit:c,credit:0});}
  lines.push({account:x.clearing,debit:0,credit:x.gross});
  return {lines};
}

// ── Clearing view (§2.6) ──
export type AccClearingDay={date:string;tenders:number;settled:number;balance:number};
export type AccClearingOpen={date:string;open:number;age:number;band:'ok'|'amber'|'red'};
export function accBankClearing(s:State,code:'1010'|'1020',asOf:string):{code:string;days:AccClearingDay[];balance:number;open:AccClearingOpen[]}{
  const m=new Map<string,{t:number;st:number}>();
  for(const v of accVouchersUpTo(s,asOf))for(const e of v.entries)if(e.account===code){const d=m.get(v.date)??{t:0,st:0};d.t+=e.debit;d.st+=e.credit;m.set(v.date,d);}
  let bal=0;const out:AccClearingDay[]=[];
  for(const [date,d] of [...m].sort((a,b)=>a[0]<b[0]?-1:1)){bal+=d.t-d.st;out.push({date,tenders:d.t,settled:d.st,balance:bal});}
  // FIFO: settlements clear the oldest tender days first.
  let pool=out.reduce((a,d)=>a+d.settled,0);const open:AccClearingOpen[]=[];
  for(const d of out){const take=Math.min(pool,d.tenders);pool-=take;const left=d.tenders-take;
    if(left>0){const age=days(d.date,asOf);open.push({date:d.date,open:left,age,band:age>7?'red':age>3?'amber':'ok'});}}
  return {code,days:out,balance:bal,open};
}

// ── Reconciliation report (§2.6) ──
export type AccBankRecon={ledger:string;asOf:string;booksBalance:number;issuedNotPresented:AccBankBookLine[];depositsNotCredited:AccBankBookLine[];
  bankOnly:BankStatementRow[];issuedTotal:number;depositsTotal:number;bankOnlyTotal:number;computedStatement:number;statementBalance:number;difference:number};
export function accBankRecon(s:State,ledger:string,asOf:string):AccBankRecon{
  const x=accBankSlice(s);
  let books=0;for(const v of accVouchersUpTo(s,asOf))for(const e of v.entries)if(e.account===ledger)books+=e.debit-e.credit;
  const refs=accBankMatchedRefs(s);
  const unmatched=accBankBookLines(s,ledger,asOf).filter(l=>{const mm=refs.get(refKey(l.ref));return !mm||mm.clearedDate>asOf;});
  const issued=unmatched.filter(l=>l.amount<0),deposits=unmatched.filter(l=>l.amount>0);
  const rows=x.rows.filter(r=>r.bankLedger===ledger&&r.date<=asOf);
  const bankOnly=rows.filter(r=>r.status==='unmatched');
  const issuedTotal=-issued.reduce((a,l)=>a+l.amount,0),depositsTotal=deposits.reduce((a,l)=>a+l.amount,0),bankOnlyTotal=bankOnly.reduce((a,r)=>a+rowAmt(r),0);
  const computed=books+issuedTotal-depositsTotal+bankOnlyTotal;
  // Statement balance as of asOf: the last row's balance, else the import opening + Σ rows.
  const sorted=[...rows].sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:0);
  const last=sorted.at(-1);let stmt=0;
  if(last?.balance!==undefined&&rows.every(r=>r.balance!==undefined))stmt=last.balance;
  else{const firstImp=x.imports.filter(i=>i.bankLedger===ledger).sort((a,b)=>a.from<b.from?-1:1)[0];stmt=(firstImp?.openingBalance??0)+rows.reduce((a,r)=>a+rowAmt(r),0);}
  return {ledger,asOf,booksBalance:books,issuedNotPresented:issued,depositsNotCredited:deposits,bankOnly,issuedTotal,depositsTotal,bankOnlyTotal,
    computedStatement:computed,statementBalance:stmt,difference:stmt-computed};
}
export const ACC_BANK_RECON_CSV_HEADER=['Section','Date','Voucher / row','Narration','Amount'];
export function accBankReconCsv(r:AccBankRecon):string{
  const q=(v:string|number)=>{const t=guard(String(v));return /[",\r\n]/.test(t)?'"'+t.split('"').join('""')+'"':t;};
  const rs=(n:number)=>(n/100).toFixed(2);const L:string[][]=[ACC_BANK_RECON_CSV_HEADER];
  L.push(['Balance per books',r.asOf,'','',rs(r.booksBalance)]);
  for(const l of r.issuedNotPresented)L.push(['Add: issued, not presented',l.date,l.no,l.narration,rs(-l.amount)]);
  for(const l of r.depositsNotCredited)L.push(['Less: deposited, not credited',l.date,l.no,l.narration,rs(-l.amount)]);
  for(const b of r.bankOnly)L.push(['Bank only (unmatched)',b.date,b.id,b.narration,rs(b.credit-b.debit)]);
  L.push(['Balance per statement',r.asOf,'','',rs(r.statementBalance)],['Difference','','','',rs(r.difference)]);
  return L.map(x=>x.map(q).join(',')).join('\r\n')+'\r\n';
}
