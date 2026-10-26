// A03b-05 · Tally voucher ids and provisional exports (server: Hcc.Domain.Accounting.VoucherExport + AccountingQueries.TallyAsync).
// Every voucher carries REMOTEID = GUID = HCC-<identity>-<source record> (demo identity DEMO), the same in every export and unique per
// voucher, so a re-import replaces instead of duplicating; a re-exported day no longer has to be deleted in Tally first, except a day an
// export before the ids took (LEGACY). Production preview: while the server reports pending accounting events, every unlocked date is
// PROVISIONAL with the count, and the export flags it so that the first export after the events are processed carries it again.
export async function register({test,M,assert}){
  const ids=xml=>[...xml.matchAll(/<VOUCHER REMOTEID="([^"]+)"[^>]*>\n    <DATE>\d{8}<\/DATE><GUID>([^<]+)<\/GUID>/g)].map(m=>{assert.equal(m[1],m[2],'REMOTEID = GUID');return m[1];});
  const s0=M.seed(),D=s0.date,Y=M.accDates(s0)[1];

  test('[TALLY] A03b-05 every voucher carries REMOTEID = GUID = HCC-DEMO-<source>, the same in every export and unique per voucher',()=>{
    const p=M.accTallyXml(s0,Y,D),list=ids(p.xml),vs=[Y,D].flatMap(d=>M.accVouchers(s0,d));
    assert.equal(list.length,p.count);assert.equal(new Set(list).size,list.length,'unique per voucher');
    assert.deepEqual(list,vs.map(v=>'HCC-DEMO-'+v.source.id));
    assert.equal(p.xml.split('\n')[2],M.ACC_TALLY_ID_NOTE);assert.match(M.ACC_TALLY_ID_NOTE,/Overwrite vouchers when a voucher with same GUID exists/);
    assert.deepEqual(ids(M.accTallyXml(s0,Y,D).xml),list,'same ids on a second export');
    // A later charge adds a voucher with its own id; every earlier voucher keeps its id.
    const s=M.execute(s0,'owner',{type:'CHARGE',id:'HCC-1041',label:'Laundry',amount:50000});
    const again=ids(M.accTallyXml(s,Y,D).xml);
    assert.ok(list.every(x=>again.includes(x)));assert.equal(again.length,list.length+1);assert.equal(new Set(again).size,again.length);
    assert.equal(M.accTallyRemoteId('DEMO','a&b'),'HCC-DEMO-a&b');
    // The recorded export says its vouchers carried ids.
    assert.equal(M.execute(s0,'accountant',{type:'ACC_TALLY_EXPORT',from:D,to:D}).accountingX.tallyExports.at(-1).ids,true);
  });

  test('[TALLY] A03b-05 a re-exported day no longer has to be deleted first; a day exported before the ids gets a LEGACY note',()=>{
    assert.doesNotMatch(M.accTallyReexportNote(D,'x'),/delete/i);
    assert.match(M.accTallyReexportNote(D,'Wrong folio'),/^RE-EXPORT · 2026-10-24 changed after it was exported \(day reopened: Wrong folio\)\. Its vouchers keep their REMOTEID/);
    const old={...s0,accountingX:{...M.accSlice(s0),tallyExports:[{id:'TLY-old',at:s0.clock,by:'accountant',from:D,to:D,dates:[D],reexport:[],provisional:[],count:1}]}};
    const p=M.accTallyXml(old,D,D);
    assert.deepEqual(p.legacy,[D]);assert.ok(p.xml.includes(M.accTallyNote(M.accTallyLegacyNote(D))));
    assert.match(M.accTallyLegacyNote(D),/delete that day's HCC vouchers from it in Tally before importing this file/);
    assert.deepEqual(M.accTallyXml(old,Y,Y).legacy,[]);
    // Once exported with ids the day still names the earlier, id-less file (Tally may hold those copies).
    const after=M.execute(old,'accountant',{type:'ACC_TALLY_EXPORT',from:D,to:D});
    assert.deepEqual(M.accTallyXml(after,D,D).legacy,[D]);
  });

  test('[TALLY] A03b-05 production preview: pending events on the server make every unlocked date PROVISIONAL with the count and flag it; a locked date is not',()=>{
    const day=(d,status,exceptions=[])=>({status,vouchers:M.accVouchers(s0,d),exceptions,checks:[]});
    const pending={id:'pending',severity:'error',kind:'pending',title:'Accounting events still being processed',detail:'3 posting event(s) for this property are not yet in the vouchers. Wait a moment, then lock.'};
    const server=ex=>({...s0,accountingX:{...M.accSlice(s0),server:{from:Y,to:D,days:{[Y]:day(Y,'Locked'),[D]:day(D,'Open',ex)},opening:[],mapRows:[]}}});
    let s=server([pending]);
    let p=M.accTallyXml(s,Y,D);
    assert.deepEqual([p.pending,p.lagging,p.provisional,p.reopened],[3,[D],[D],[]]);
    assert.ok(p.xml.includes(M.accTallyNote(M.accTallyPendingNote(D,3))));assert.doesNotMatch(p.xml,/PROVISIONAL · 2026-10-23/);
    assert.ok(ids(p.xml).every(x=>x.startsWith('HCC-SERVER-')),'the preview stands in for the server identity');
    s=M.execute(s,'accountant',{type:'ACC_TALLY_EXPORT',from:Y,to:D});
    assert.deepEqual([M.accSlice(s).reexport[D].reason,M.accSlice(s).reexport[D].reopen],['3 accounting event(s) were still pending',undefined]);
    // Still pending: carried again and still provisional, the flag stays. Processed: carried once more (complete) and the flag clears.
    s=M.execute(s,'accountant',{type:'ACC_TALLY_EXPORT',from:Y,to:Y});
    assert.ok(M.accSlice(s).reexport[D]);
    s={...s,accountingX:{...M.accSlice(s),server:{...M.accSlice(s).server,days:{...M.accSlice(s).server.days,[D]:day(D,'Open')}}}};
    p=M.accTallyXml(s,Y,Y);
    assert.deepEqual([p.reexport,p.provisional,p.pending],[[D],[],0]);
    assert.ok(p.xml.includes(M.accTallyNote(M.accTallyReexportProvisionalNote(D,'3 accounting event(s) were still pending'))));
    s=M.execute(s,'accountant',{type:'ACC_TALLY_EXPORT',from:Y,to:Y});
    assert.equal(M.accSlice(s).reexport[D],undefined);
    assert.deepEqual(M.accTallyXml(s,Y,Y).reexport,[]);
  });
}
