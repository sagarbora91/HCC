// Polish fixes 2: V9-2 phone sizes (S4), Day pack for the Accountant (DEC-063 G4), Accounting voucher export for the Cashier (DEC-063 G13). Names prefixed [PX].
import {readFile} from 'node:fs/promises';
export function register({test,M,assert}){
  test('[PX] V9-2 phone sheet labels 16 px, Cashier count switch 48 px, badges and DEMO pill >= 14 px (rules out-rank the 14 px / 44 px ones)',async()=>{
    const css=await readFile('src/styles/z-touch.css','utf8');
    const i=css.indexOf('@media (max-width:767px)');assert.ok(i>0);const blk=css.slice(i);
    assert.ok(/\.modal\.m-sheet \.form label[^{]*\{font-size:16px\}/.test(blk),'sheet label 16');
    assert.ok(/\.mm-seg button[^{]*\{min-height:var\(--hcc-touch-primary,48px\)\}/.test(blk),'mm-seg 48');
    assert.ok(/\.mm-seg b\{font-size:14px\}/.test(blk)&&/\.m-chips button b[^{]*\{font-size:14px\}/.test(blk),'badges 14');
    assert.ok(/\.m-demo-pill\{font-size:14px\}/.test(blk),'demo 14');
    const mob=await readFile('src/styles/mobile.css','utf8');assert.ok(/\.modal\.m-sheet \.form label\{font-size:14px\}/.test(mob),'premise: the 14 px rule exists');
    const sheets=await readFile('src/mobile/admin/sheets.tsx','utf8');assert.ok(/From \(business date\)/.test(sheets));
    const cash=await readFile('src/mobile/money/Cashier.tsx','utf8');assert.ok(/className="mm-seg"/.test(cash));});

  const USERS={owner:'Owner',manager:'Manager',accountant:'Accountant',cashier:'Cashier',reception:'Reception',housekeeper:'Housekeeping',maint:'Maintenance'};
  test('[PX] G4 · Day pack is deliberate: Owner, Manager and Accountant see it; Cashier, Reception, Housekeeping, Maintenance do not',async()=>{
    const s=M.seed();
    for(const u of Object.keys(USERS))assert.equal(M.rbacCanDayPack(s,u),['owner','manager','accountant'].includes(u),u);
    assert.ok(/rbacCanDayPack/.test(await readFile('src/features/exports/DayPack.tsx','utf8')),'DayPackButton gated');});

  test('[PX] G13 · Accounting voucher CSV/Excel export: not the Cashier; Owner, Manager, Accountant keep it; helper refuses',async()=>{
    const s=M.seed();
    for(const u of Object.keys(USERS))assert.equal(M.rbacCanExportVouchers(s,u),['owner','manager','accountant'].includes(u),u);
    const parts=await readFile('src/features/accounting/parts.tsx','utf8');
    assert.ok(/rbacCanExportVouchers/.test(parts),'button and helper gated');
    assert.ok(/export function accExport\(.*uid:string\)\{\s*if\(!rbacCanExportVouchers/.test(parts),'accExport takes the acting user and refuses');});
}
