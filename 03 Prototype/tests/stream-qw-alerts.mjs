// F4-T3 qw-alerts · reorder alerts, printers (test page, offline). Names prefixed [QW].
export function register({test,M,assert}){
  const {seed,execute,qwReorderAlerts,qwReorderLine,invAlerts,sysPrinters,sysPrintersOffline,sysTestPageText}=M;
  test('[QW] reorder alerts equal the inventory alerts; the line names up to three items',()=>{
    const s=seed(),a=qwReorderAlerts(s);assert.equal(a.length,invAlerts(s).length);
    const line=qwReorderLine(a);if(a.length)assert.match(line,/below reorder level/);else assert.equal(line,'Stock is above reorder levels');
    if(a.length>3)assert.match(line,/and \d+ more/);});
  test('[QW] printers: two demo printers, test page records time, offline test fails and shows the banner list',()=>{
    let s=seed();assert.equal(sysPrinters(s).length,2);assert.equal(sysPrintersOffline(s).length,0);
    s=execute(s,'owner',{type:'QW_PRINTER_TEST',id:'prt-front',key:'t1'});
    const p=sysPrinters(s).find(x=>x.id==='prt-front');assert.equal(p.lastTestOk,true);assert.ok(p.lastTestAt);
    assert.match(sysTestPageText(s,p),/Printer test page/);
    s=execute(s,'owner',{type:'QW_PRINTER_SET',id:'prt-kitchen',online:false,key:'t2'});assert.equal(sysPrintersOffline(s).length,1);
    s=execute(s,'owner',{type:'QW_PRINTER_TEST',id:'prt-kitchen',key:'t3'});assert.equal(sysPrinters(s).find(x=>x.id==='prt-kitchen').lastTestOk,false);
    assert.throws(()=>execute(s,'owner',{type:'QW_PRINTER_TEST',id:'nope',key:'t4'}),e=>e.code==='NOT_FOUND');
    assert.throws(()=>execute(s,'reception',{type:'QW_PRINTER_TEST',id:'prt-front',key:'t5'}),e=>e.code==='FORBIDDEN');});
}
