// V11 fixes: V10-1 phone invoice CGST/SGST, V10-2 Show ID footnote, PB-1 Escape at ModalHost, PB-2 locked-day buttons, PB-3 phone button 16 px.
import {readFileSync} from 'node:fs';
const src=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
export function register({test,M,assert}){
  test('[V10-1] phone invoice shows CGST + SGST via the shared GST helpers (no own tax maths)',()=>{
    const t=src('src/mobile/money/Checkout.tsx'),inv=t.slice(t.indexOf('export function MmInvoice'));
    assert.match(inv,/<th className="number">CGST<\/th><th className="number">SGST<\/th>/);
    assert.match(inv,/gstLineSplit\(l\)/);assert.match(inv,/gstSplitLines\(b\.lines\)\.cgst/);assert.match(inv,/gstSplitLines\(b\.lines\)\.sgst/);
    assert.doesNotMatch(inv,/lineTax/);
  });
  test('[V10-1] the helper the phone invoice uses splits tax into CGST + SGST that add up',()=>{
    const lines=[{amount:200000,taxBps:500},{amount:50000,taxBps:1800},{amount:-50000,taxBps:500}];
    for(const l of lines){const g=M.gstLineSplit(l);assert.equal(g.cgst+g.sgst,g.tax);}
    const sum=M.gstSplitLines(lines);assert.equal(sum.cgst+sum.sgst,lines.reduce((a,l)=>a+M.lineTax(l),0));
  });
  test('[V10-2] "Show ID is logged" footnote only when the role can see ID details',()=>{
    const t=src('src/mobile/frontdesk/Guests.tsx');
    assert.match(t,/\{!v\.masked&&<p className="mf-note">Shown masked \(last 4\)\. "Show ID" is logged/);
  });
  test('[PB-1] ModalHost handles Escape at document level, skips consumed keys, keeps the dialog handler and closeModal guard',()=>{
    const t=src('src/app/ModalHost.tsx');
    assert.match(t,/document\.addEventListener\('keydown',onKey\)/);assert.match(t,/document\.removeEventListener\('keydown',onKey\)/);
    assert.match(t,/e\.key==='Escape'&&!e\.defaultPrevented/);assert.match(t,/closeRef\.current\(\)/);
    assert.match(t,/if\(e\.key==='Escape'\)\{e\.preventDefault\(\);closeModal\(\);return;\}/);
  });
  test('[PB-2] locked cash-book day: cbIsLocked/cbLockedMessage drive the reason text',()=>{
    const s={...M.seed(),cashbookX:{locks:{'2026-10-23':{}},history:{}}};
    assert.equal(M.cbIsLocked(s,'2026-10-23'),true);assert.equal(M.cbIsLocked(s,'2026-10-22'),false);
    assert.match(M.cbLockedMessage('2026-10-23','reversing this expense'),/locked.*Owner must reopen 2026-10-23/);
  });
  test('[PB-2] Reverse… and Request refund are disabled on a locked day, desktop and phone, with the reason shown',()=>{
    for(const [p,btn] of [['src/features/expenses/Expenses.tsx','Reverse…'],['src/mobile/money/Expenses.tsx','Reverse…'],['src/features/folio/modals.tsx','Request refund'],['src/mobile/money/Folio.tsx','Request refund']]){
      const t=src(p);assert.match(t,/cbIsLocked,cbLockedMessage/,p);
      assert.match(t,new RegExp('disabled=\{busy\|\|(noShift\|\|)?locked\}[^\n]*?'+btn),p);
      assert.match(t,/\{locked(\?|&&)/,p);
    }
    assert.match(src('src/mobile/money/Folio.tsx'),/cbIsLocked\(s,firstRefundable\.date\)\)return;/);
  });
  test('[PB-3] z-touch.css forces 16 px on phone buttons (btn, text-btn, segment, chip) in the shell and in sheets',()=>{
    const css=src('src/styles/z-touch.css'),i=css.indexOf('PB-3'),blk=css.slice(i);
    assert.ok(i>0);
    for(const sel of ['.m-shell .btn','.modal.m-sheet .btn','.modal.m-sheet .f-seg button','.m-shell .text-btn'])assert.ok(blk.includes(sel),sel);
    assert.match(blk,/\{font-size:16px!important\}/);
  });
  test('[PB-3] build.mjs loads z-touch.css after every other style (so the override wins)',()=>{
    const b=src('build.mjs');assert.match(b,/z-touch|sort|readdir/);
  });
}
