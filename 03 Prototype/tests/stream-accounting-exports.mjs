// ACC-3 lane acc3-exports — Excel / CSV / bulk-zip exports of the accounting reports (contract §5). Names prefixed [ACC3-EXPORTS].
// The §2–§3 report objects are hand-built to the contract shapes (the report lanes had not landed); the ACC-1 statements are real.
import {build} from 'esbuild';
await build({stdin:{contents:"export * from './src/domain/streams/accounting-exports';export * from './src/app/zip';export {buildXlsx,dateSerial} from './src/app/xlsx';export {accStatementCsv,accTrialBalance6,accTrialBalance6Csv,accStatement} from './src/domain/streams/accounting-reports';",resolveDir:'.',loader:'ts'},bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/acc-exports.mjs',logLevel:'error'});
await build({entryPoints:['src/app/native.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/native-acc3.mjs',logLevel:'error',external:['react']});
const X=await import('./.generated/acc-exports.mjs');
const dec=new TextDecoder();
const PL={from:'2026-04-01',to:'2026-04-30',provisional:true,
  revenue:{lines:[{code:'4000',name:'Room revenue',paise:30000000,group:'Sales Accounts'},{code:'4900',name:'=HYPERLINK("x")',paise:-100000,group:'Sales Accounts'}],total:29900000},
  otherIncome:{lines:[],total:0},
  expenses:{consumption:{name:'Consumption',lines:[],total:0},employee:{name:'Employee cost',lines:[{code:'5100',name:'Salaries',paise:1000000,group:'Indirect Expenses'}],total:1000000},finance:{name:'Finance',lines:[],total:0},depreciation:{name:'Depreciation',lines:[],total:0},other:{name:'Other',lines:[],total:0},total:1000000},
  profitBeforeTax:28900000,byTallyGroup:[{group:'SA',name:'Sales Accounts',paise:29900000}]};
const AGE={asOf:'2026-04-30',side:'receivable',rows:[{account:'1110/CMP-A',party:'CMP-A',name:'Deccan Agro',control:'1110',bills:[{ref:'SAL/1',date:'2026-04-10',due:'2026-04-25',amount:7080000,paid:0,open:7080000,days:20}],open:7080000,b0_30:7080000,b31_60:0,b61_90:0,b90:0}],totals:{open:7080000,b0_30:7080000,b31_60:0,b61_90:0,b90:0}};
const GST={month:'2026-04',output:{rate5:100,rate18:200,b2b:1,b2cs:2,advances11A:3,advances11B:4,creditNotes:5},itc:{cgst:1,sgst:1,igst:0},rcm:0,setoff:{output:{igst:0,cgst:15,sgst:15},credit:{igst:0,cgst:5,sgst:5},cashAvailable:0,use:{igst:{igst:0,cgst:0,sgst:0},cgst:{igst:0,cgst:5,sgst:0},sgst:{igst:0,cgst:0,sgst:5}},cash:{igst:0,cgst:10,sgst:10},creditLeft:{igst:0,cgst:0,sgst:0},payable:20,challan:20},cashPaid:2890000,tcsCredit:0,cashLedgerBalance:0,ties:{gstr1:true,gstr3b:true,differences:[]}};
const BS={asOf:'2026-04-30',provisional:false,liabilities:{ownersFunds:{name:"Owner's funds",lines:[{code:'3100',name:'Capital',paise:100,}],total:100},nonCurrent:{name:'Non-current',lines:[],total:0},current:{name:'Current',lines:[],total:0},total:100},assets:{nonCurrent:{name:'Fixed assets',lines:[],total:0},current:{name:'Current assets',lines:[{code:'1000',name:'Cash',paise:100}],total:100},total:100},balanced:true,suspense:0,openingDifference:0};
const CR={fy:'2026-27',cashReceipts:10,totalReceipts:1000,cashPayments:20,totalPayments:100,receiptsPct:100,paymentsPct:2000 /* basis points, as accCashRatio (integration) */,flagged:true};
/** Sheet XML parts of an xlsx, via the stored-zip reader. */
const parts=b=>Object.fromEntries(X.unzipStored(b).map(e=>[e.name,dec.decode(e.data)]));
export function register({test,M,assert}){
  test('[ACC3-EXPORTS] zip: store-only entries round-trip with CRC, names are sanitised and unique, same input gives same bytes',()=>{
    const b=X.zipFiles([{name:'a.csv',data:'x,y\r\n1,2'},{name:'A.csv',data:'dup'},{name:'../bad:name?.csv',data:new Uint8Array([1,2,3])}]);
    const e=X.unzipStored(b);assert.deepEqual(e.map(x=>x.name),['a.csv','A (2).csv','-bad-name-.csv']);
    assert.equal(dec.decode(e[0].data),'x,y\r\n1,2');assert.deepEqual([...e[2].data],[1,2,3]);
    assert.deepEqual(X.zipFiles([{name:'a.csv',data:'x,y\r\n1,2'},{name:'A.csv',data:'dup'},{name:'../bad:name?.csv',data:new Uint8Array([1,2,3])}]),b);
    const bad=b.slice();bad[40]^=0xff;assert.throws(()=>X.unzipStored(bad),/CRC|zip/);
    assert.equal(X.unzipStored(X.zipFiles([])).length,0);
  });
  test('[ACC3-EXPORTS] file names are <property code>-<report>-<from>-<to>.<ext>',()=>{
    assert.equal(X.accExportName('HCC','profit-and-loss','2026-04-01','2026-04-30','xlsx'),'HCC-profit-and-loss-2026-04-01-2026-04-30.xlsx');
    assert.equal(X.accExportName('HCC','balance-sheet','2026-04-30','2026-04-30','csv'),'HCC-balance-sheet-2026-04-30.csv');
    assert.equal(X.accPlXlsx('HLP',PL).name,'HLP-profit-and-loss-2026-04-01-2026-04-30.xlsx');
  });
  test('[ACC3-EXPORTS] P&L workbook: two sheets, bold title with range and PROVISIONAL, money #,##0.00 numbers, dates as dates, header frozen under the title block',()=>{
    const f=X.accPlXlsx('HCC',PL),p=parts(f.bytes);
    assert.match(p['xl/workbook.xml'],/name="Profit and loss"/);assert.match(p['xl/workbook.xml'],/name="By Tally group"/);
    const s1=p['xl/worksheets/sheet1.xml'];
    assert.match(s1,/Profit &amp; loss · 2026-04-01 to 2026-04-30 · PROVISIONAL/);
    assert.match(s1,/<c r="A1" s="7" t="inlineStr">/,'title row bold (style 7)');
    assert.match(s1,/<c r="B3" s="3"><v>46113<\/v>/,'From is a date serial');
    assert.equal(X.dateSerial('2026-04-01'),46113);
    assert.match(s1,/<v>300000<\/v>/,'room revenue 3,00,000.00 as a number');assert.match(s1,/s="6"><v>300000<\/v>/);
    assert.match(s1,/<pane ySplit="\d+" topLeftCell="A\d+" activePane="bottomLeft" state="frozen"\/>/);
    assert.match(p['xl/styles.xml'],/<cellXfs count="8">/);assert.match(p['xl/styles.xml'],/numFmtId="4"/);
    assert.match(s1,/Profit before tax/);assert.match(s1,/<v>289000<\/v>/);
    assert.equal(f.mime,'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
  });
  test('[ACC3-EXPORTS] every report builds a workbook with its sheets (balance sheet, cash ratio, ageing, GST summary)',()=>{
    assert.match(parts(X.accBalanceSheetXlsx('HCC',BS).bytes)['xl/workbook.xml'],/Balance sheet/);
    assert.match(parts(X.accCashRatioXlsx('HCC',CR).bytes)['xl/worksheets/sheet1.xml'],/FLAGGED/);
    const a=parts(X.accAgeingXlsx('HCC',AGE).bytes);assert.match(a['xl/workbook.xml'],/Outstanding/);assert.match(a['xl/workbook.xml'],/Bills/);assert.match(a['xl/worksheets/sheet2.xml'],/SAL\/1/);
    assert.match(parts(X.accGstSummaryXlsx('HCC',GST).bytes)['xl/worksheets/sheet1.xml'],/Set-off · payable in cash/);
    assert.match(parts(X.accBalanceSheetXlsx('HCC',BS).bytes)['xl/worksheets/sheet1.xml'],/not the statutory statement/);
  });
  test('[ACC3-EXPORTS] CSV: same rows as the sheet, CRLF, money with two decimals, formula-looking text neutralised',()=>{
    const c=X.accPlCsv('HCC',PL),t=c.text;
    assert.ok(t.includes('\r\n'));assert.match(t,/4000,Room revenue,300000\.00/);assert.match(t,/PROVISIONAL/);
    assert.match(t,/4900,"'=HYPERLINK\(""x""\)"/,'a vendor-typed formula gets a leading apostrophe');
    assert.ok(!/(^|,)=HYPERLINK/m.test(t));
    assert.match(t,/-1000\.00/,'a negative number stays a number');
    assert.equal(X.accCashRatioCsv('HCC',CR).name,'HCC-cash-ratio-2026-27.csv');
  });
  test('[ACC3-EXPORTS] statements of the real seed export to Excel and CSV with matching totals',()=>{
    const s=M.seed(),st=X.accStatement(s,'1100','2026-10-23','2026-10-24');assert.ok(st.rows.length>0);
    const f=X.accStatementXlsx('HCC',st),sh=parts(f.bytes)['xl/worksheets/sheet1.xml'];
    assert.match(f.name,/^HCC-statement-1100-2026-10-23-2026-10-24\.xlsx$/);
    assert.ok(sh.includes(`<v>${st.debit/100}</v>`),'debit total as a number');assert.match(sh,/Closing balance/);
    const csv=X.accTableCsv(X.accStatementTable('HCC',st));assert.match(csv,/Closing balance/);
    const tb=X.accTrialBalance6(s,'2026-10-23','2026-10-24',false);
    assert.match(parts(X.accTrialBalanceXlsx('HCC',tb).bytes)['xl/worksheets/sheet1.xml'],/Total/);
  });
  test('[ACC3-EXPORTS] bulk zip: one file per party with activity, receivable / payable filters, csv and xlsx, no formula cells',()=>{
    const s=M.seed(),d='2026-10-01';
    const V=(no,type,entries)=>({no,date:d,type,narration:'=cmd|calc',source:{kind:'closing',id:'T/'+no,sub:'test'},entries:entries.map(([account,debit,credit])=>({account,debit,credit})),unmapped:[]});
    const vouchers=[V('SAL/20261001/001','Sales',[['1110/CMP-A',1000000,0],['4000',0,1000000]]),V('BIL/20261001/001','Purchase',[['5000',500000,0],['2200/V-1',0,500000]])];
    const t=M.accTotals(vouchers),sl=M.accSlice(s);
    const s2={...s,accountingX:{...sl,locks:{...(sl.locks??{}),[d]:{date:d,lockedAt:'2026-10-02T10:00',lockedBy:'owner',vouchers,debit:t.debit,credit:t.credit,count:vouchers.length}}}};
    const ledgers=X.accBulkParties(s2,'all');assert.ok(ledgers.length>0);
    const all=X.accStatementsZip(s2,{from:d,to:d,parties:'all',format:'csv',prop:'HCC'});
    assert.match(all.name,/^HCC-statements-all-2026-10-01\.zip$/);
    const e=X.unzipStored(all.bytes);assert.equal(e.length,all.files.length);
    for(const f of e){assert.match(f.name,/^HCC-statement-[\w-]+-2026-10-01\.csv$/);}
    const ent=e.map(x=>dec.decode(x.data)).join('\n');
    // the injected narration starts with '=' and must be neutralised wherever it lands
    assert.ok(!/(^|,)=cmd/m.test(ent));
    const rec=X.accStatementsZip(s2,{from:d,to:d,parties:'receivable',prop:'HCC'}),pay=X.accStatementsZip(s2,{from:d,to:d,parties:'payable',prop:'HCC'});
    assert.ok(rec.files.every(n=>/-1(100|110|115|120)-/.test(n)));assert.ok(pay.files.every(n=>/-2200-/.test(n)));
    assert.ok(rec.files.length+pay.files.length<=all.files.length);
    const x=X.accStatementsZip(s2,{from:d,to:d,parties:'all',format:'xlsx',prop:'HCC'});
    for(const f of X.unzipStored(x.bytes)){assert.match(f.name,/\.xlsx$/);assert.ok(parts(f.data)['xl/workbook.xml']);}
    assert.deepEqual(X.accStatementsZip(s2,{from:d,to:d,parties:'all',prop:'HCC'}).bytes,all.bytes,'byte-stable');
  });
  test('[ACC3-EXPORTS] saveText exists in native.ts and downloads without a byte-order mark',async()=>{
    const N=await import('./.generated/native-acc3.mjs');assert.equal(typeof N.saveText,'function');
    const seen=[];const g=globalThis;const keep={URL:g.URL.createObjectURL,doc:g.document};
    g.URL.createObjectURL=b=>{seen.push(b);return 'blob:x';};g.URL.revokeObjectURL=()=>{};g.document={createElement:()=>({click(){}})};
    try{const r=await N.saveText('x.xml','<a/>','application/xml');assert.equal(r,'downloaded');assert.equal(seen[0].size,4);}finally{g.URL.createObjectURL=keep.URL;g.document=keep.doc;}
  });
}
