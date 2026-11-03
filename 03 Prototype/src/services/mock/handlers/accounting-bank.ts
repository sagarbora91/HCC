// ACC-4 · Bank reconciliation and clearing — command handlers (ACC4-CONTRACT.md §2; PERM-ACC-016 Owner + Accountant, PERM-ACC-017 Owner).
// BANK_LAYOUT_UPSERT · BANK_IMPORT · BANK_AUTO_MATCH · BANK_MATCH · BANK_UNMATCH · BANK_IGNORE · BANK_CREATE_VOUCHER · BANK_SETTLEMENT_POST.
// Vouchers made here (B8 settlement, charges, interest) are approved manual vouchers (they post through the ACC-2 manual path) matched to
// their row at once. Contract codes travel as message prefixes (BANK_DUP_FILE, BANK_BALANCE_BREAK, BANK_MATCH_SUM, BANK_SETTLEMENT_SUM).
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {accSlice,type ACCManualVoucher} from '../../../domain/streams/accounting';
import {accMonthLocked} from '../../../domain/streams/accounting-gst';
import {accFyOfDate,accYearLocked} from '../../../domain/streams/accounting-year';
import {accBankSlice,accBankLayouts,accBankLayout,accBankImportPlan,accBankAutoMatch,accBankManualProblem,accBankSettlementLines,
  type AccBankSlice,type BankStatementLayout,type BankStatementRow,type BankMatch,type AccBankLineRef} from '../../../domain/streams/accounting-bank';

const ROLES=['Owner','Accountant'];
const LEDGER=/^1030(\/[A-Za-z0-9][A-Za-z0-9._-]{0,35})?$|^10[0-9]{2}(\/[A-Za-z0-9._-]+)?$/;
const enter=(k:Kit)=>{if(!ROLES.includes(k.user.role))k.fail('FORBIDDEN','Access denied: only the Owner or the Accountant reconciles the bank (PERM-ACC-016).');};
const slice=(k:Kit):AccBankSlice=>{const x=accBankSlice(k.s);k.s.accBankX={...x,imports:[...x.imports],rows:[...x.rows],matches:[...x.matches]};return k.s.accBankX;};
const rowOf=(k:Kit,id:unknown)=>{const r=accBankSlice(k.s).rows.find(x=>x.id===String(id??''));k.assert(r,'This statement row was not found.','NOT_FOUND');return r!;};
const setRows=(x:AccBankSlice,ids:string[],status:BankStatementRow['status'],matchId?:string)=>{x.rows=x.rows.map(r=>ids.includes(r.id)?{...r,status,...(matchId?{matchId}:{}),...(status==='unmatched'?{matchId:undefined}:{})}:r);};
const addMatch=(k:Kit,x:AccBankSlice,rowIds:string[],refs:AccBankLineRef[],rule:BankMatch['rule'],clearedDate:string)=>{
  const m:BankMatch={id:k.id('BM'),rowIds,voucherLineRefs:refs,rule,clearedDate,by:k.uid,at:k.s.clock};x.matches.push(m);setRows(x,rowIds,'matched',m.id);return m;};
/** Adds an approved manual voucher (Owner's own or the Accountant's made from a bank row: generated, not keyed) and returns its book ref. */
const addVoucher=(k:Kit,v:Omit<ACCManualVoucher,'id'|'status'|'by'|'at'|'decidedBy'|'decidedAt'|'bookedOn'>):AccBankLineRef=>{
  const a={...accSlice(k.s)},id=k.id('MV');
  a.manual=[...(a.manual??[]),{...v,id,status:'approved',by:k.uid,at:k.s.clock,decidedBy:k.uid,decidedAt:k.s.clock,bookedOn:v.date}];k.s.accountingX=a;
  return {voucherId:`manual:${id}:${v.template??'journal'}`,line:0};
};
const dayOpen=(k:Kit,date:string)=>{
  if(accYearLocked(k.s,accFyOfDate(date)))k.fail('INVALID_TRANSITION',`ACC_YEAR_LOCKED: ${date} is in a locked financial year.`);
  if(accSlice(k.s).locks?.[date])k.fail('INVALID_TRANSITION',`${date} is locked — unlock the day (Owner, reason) or post the voucher on an open date.`);
};

export const handlers:Record<string,Handler>={
  BANK_LAYOUT_UPSERT:k=>{
    if(k.user.role!=='Owner')k.fail('FORBIDDEN','Access denied: only the Owner keeps the bank statement layouts (PERM-ACC-017).');
    const l=k.cmd.layout as BankStatementLayout;k.assert(l&&typeof l==='object','Enter the layout.');
    const id=String(l.id??'').trim();k.assert(/^[a-z0-9][a-z0-9-]{0,39}$/.test(id),'Layout id: lower-case letters, digits and dashes.');
    k.assert(String(l.name??'').trim().length>0,'Name the layout.');k.assert(LEDGER.test(String(l.bankLedger??'')),'Choose a bank ledger (1030/<bank>).');
    k.assert([',',';','\t'].includes(l.delimiter),'Delimiter: comma, semicolon or tab.');
    k.assert(['DD/MM/YYYY','DD-MM-YYYY','YYYY-MM-DD','DD-MMM-YYYY'].includes(l.dateFormat),'Choose a date format.');
    k.assert(Number.isInteger(l.headerRows)&&l.headerRows>=1&&l.headerRows<=10,'Header rows: 1 to 10.');
    const c=l.columns??({} as BankStatementLayout['columns']);k.assert(c.date&&c.narration,'Map the date and narration columns.');
    k.assert((c.debit&&c.credit)||c.amount,'Map debit and credit columns, or one amount column (with a Dr / Cr column or signed).');
    for(const p of [l.refPattern,l.settlementPattern])if(p)try{new RegExp(p);}catch{k.fail('VALIDATION','A pattern is not a valid regular expression.');}
    const x=slice(k),row={...l,id,name:String(l.name).trim(),active:l.active!==false};
    x.layouts=[...(x.layouts??[]).filter(o=>o.id!==id),row];
    return `Bank statement layout ${row.name} (${id}) saved · ${accBankLayouts(k.s).length} layout(s)`;
  },
  BANK_IMPORT:k=>{
    enter(k);const {s,cmd,uid}=k;
    const ledger=String(cmd.bankLedger??'');k.assert(/^1030\/./.test(ledger),'Choose the bank account (1030/<bank>).');
    const lay=accBankLayout(s,String(cmd.layoutId??'generic'));k.assert(lay&&lay.active,'Choose an active statement layout.','NOT_FOUND');
    const text=String(cmd.text??'');k.assert(text.length>0&&text.length<=2_000_000,'Choose a statement file (up to 2 MB).');
    const p=accBankImportPlan(s,ledger,text,lay!);
    if(p.error)k.fail('VALIDATION',p.error.message);
    if(p.duplicateFile)k.fail('VALIDATION','BANK_DUP_FILE: this statement file was already imported.');
    if(p.lockedRow)k.fail('INVALID_TRANSITION',`ACC_YEAR_LOCKED: row ${p.lockedRow.line} (${p.lockedRow.date}) is in a locked financial year.`);
    const keep=cmd.keepDuplicates===true,take=p.rows.filter(r=>keep||!p.overlaps.includes(r.line));
    const x=slice(k),iid=k.id('BSI');
    x.imports.push({id:iid,bankLedger:ledger,layoutId:lay!.id,fileName:String(cmd.fileName??'statement.csv').slice(0,200),fileHash:p.hash,
      from:p.rows[0].date,to:p.rows.at(-1)!.date,openingBalance:p.openingBalance,closingBalance:p.closingBalance,rows:take.length,importedBy:uid,importedAt:s.clock});
    take.forEach((r,i)=>x.rows.push({id:`${iid}-${String(i+1).padStart(4,'0')}`,importId:iid,bankLedger:ledger,date:r.date,narration:r.narration,
      ...(r.ref?{ref:r.ref}:{}),debit:r.debit,credit:r.credit,...(r.balance!==undefined?{balance:r.balance}:{}),status:'unmatched'}));
    const skipped=p.rows.length-take.length;
    return `Statement ${iid} imported for ${ledger} · ${take.length} row(s) ${p.rows[0].date} → ${p.rows.at(-1)!.date}${skipped?` · ${skipped} duplicate row(s) skipped`:''} · closing ${money(p.closingBalance)}`;
  },
  BANK_AUTO_MATCH:k=>{
    enter(k);const ledger=String(k.cmd.bankLedger??'');const a=accBankAutoMatch(k.s,ledger);const x=slice(k);
    for(const p of a.proposals)addMatch(k,x,p.rowIds,p.lines.map(l=>l.ref),p.rule,p.clearedDate);
    return `Auto-match ${ledger} · ${a.proposals.length} matched · ${a.settlements.length} settlement(s) to post · ${a.ambiguous.length} left for manual (two or more candidates)`;
  },
  BANK_MATCH:k=>{
    enter(k);const rowIds=(k.cmd.rowIds??[]).map(String),refs=(k.cmd.refs??[]) as AccBankLineRef[];
    const why=accBankManualProblem(k.s,rowIds,refs);if(why)k.fail(/^BANK_MATCH_SUM|^ACC_YEAR/.test(why)?'INVALID_TRANSITION':'VALIDATION',why);
    const rows=rowIds.map((id:string)=>rowOf(k,id)),cleared=rows.map((r:BankStatementRow)=>r.date).sort().at(-1)!;
    const x=slice(k),m=addMatch(k,x,rowIds,refs,'manual',cleared);
    return `Matched by hand ${m.id} · ${rowIds.length} row(s) to ${refs.length} book line(s) · cleared ${cleared}`;
  },
  BANK_UNMATCH:k=>{
    enter(k);const m=accBankSlice(k.s).matches.find(v=>v.id===String(k.cmd.matchId??'')&&!v.undoneAt);k.assert(m,'This match was not found.','NOT_FOUND');
    if(accMonthLocked(k.s,m!.clearedDate))k.fail('INVALID_TRANSITION',`Month ${m!.clearedDate.slice(0,7)} is locked — the match stays (contract §2.3).`);
    if(accYearLocked(k.s,accFyOfDate(m!.clearedDate)))k.fail('INVALID_TRANSITION',`ACC_YEAR_LOCKED: ${m!.clearedDate} is in a locked financial year.`);
    const x=slice(k);x.matches=x.matches.map(v=>v.id===m!.id?{...v,undoneAt:k.s.clock}:v);setRows(x,m!.rowIds,'unmatched');
    return `Match ${m!.id} undone · ${m!.rowIds.length} row(s) unmatched${m!.rule==='created'?' (the created voucher stays — void it if wrong)':''}`;
  },
  BANK_IGNORE:k=>{
    enter(k);const r=rowOf(k,k.cmd.rowId);k.assert(r.status==='unmatched','Only an unmatched row can be ignored.','INVALID_TRANSITION');
    const reason=String(k.cmd.reason??'').trim();k.assert(reason.length>=3,'A reason is required to ignore a statement row.');
    const x=slice(k);setRows(x,[r.id],'ignored');return `Statement row ${r.id} ignored · ${reason}`;
  },
  /** Rule 5: bank charges (Dr charge ledger · Cr bank) or interest / other credits (Dr bank · Cr income ledger) made from a row. */
  BANK_CREATE_VOUCHER:k=>{
    enter(k);const r=rowOf(k,k.cmd.rowId);k.assert(r.status==='unmatched','This row is already matched or ignored.','INVALID_TRANSITION');
    const ledger=String(k.cmd.ledger??(r.debit?'5080':'4960'));
    if(ledger==='9999')k.fail('VALIDATION','An unknown credit cannot go to 9999 Suspense — the row stays unmatched until its ledger is known.');
    k.assert(/^\d{4}(\/[A-Za-z0-9._-]+)?$/.test(ledger),'Choose a ledger.');dayOpen(k,r.date);
    const amt=r.debit||r.credit,out=r.debit>0;
    const narration=String(k.cmd.narration??'').trim()||`${out?'Bank charge':'Bank credit'} · ${r.narration}`.slice(0,300);
    const ref=addVoucher(k,{date:r.date,type:out?'Payment':'Receipt',narration,reference:r.ref??r.id,template:out?'bank-charges':'interest-income',
      lines:out?[{account:r.bankLedger,debit:0,credit:amt},{account:ledger,debit:amt,credit:0}]:[{account:r.bankLedger,debit:amt,credit:0},{account:ledger,debit:0,credit:amt}]});
    const x=slice(k),m=addMatch(k,x,[r.id],[ref],'created',r.date);
    return `${out?'Payment':'Receipt'} ${ref.voucherId.split(':')[1]} created from row ${r.id} · ${money(amt)} ${out?'Dr':'Cr'} ${ledger} · matched ${m.id}`;
  },
  /** B8 (§2.5): Dr bank net · Dr 5080 MDR (+ GST unless ITC → 1310 / 1320) · Cr 1010 / 1020 gross; matched to its row. */
  BANK_SETTLEMENT_POST:k=>{
    enter(k);const r=rowOf(k,k.cmd.rowId);k.assert(r.status==='unmatched'&&r.credit>0,'Choose an unmatched credit row.','INVALID_TRANSITION');
    const clearing=String(k.cmd.clearing??'');k.assert(clearing==='1010'||clearing==='1020','Clearing ledger: 1010 UPI or 1020 card.');
    const n=(v:unknown)=>v==null?0:Number(v);
    const res=accBankSettlementLines({bankLedger:r.bankLedger,net:r.credit,clearing:clearing as '1010'|'1020',gross:n(k.cmd.gross??r.credit),mdr:n(k.cmd.mdr),mdrGst:n(k.cmd.mdrGst),itc:k.cmd.itc===true});
    if('error' in res)k.fail('VALIDATION',res.error);
    dayOpen(k,r.date);
    const lines=(res as {lines:{account:string;debit:number;credit:number}[]}).lines;
    const ref=addVoucher(k,{date:r.date,type:'Journal',narration:`${clearing==='1010'?'UPI':'Card'} settlement · ${r.narration}`.slice(0,300),reference:r.ref??r.id,template:'journal',lines});
    const x=slice(k),m=addMatch(k,x,[r.id],[ref],'created',r.date);
    return `Settlement journal ${ref.voucherId.split(':')[1]} · Dr ${r.bankLedger} ${money(r.credit)} · Cr ${clearing} ${money(n(k.cmd.gross??r.credit))} · matched ${m.id}`;
  },
};
