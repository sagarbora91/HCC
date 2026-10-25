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
}
