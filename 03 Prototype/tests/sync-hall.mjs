// F6-H3 · hall booking in the production sync layer: GET /hall/snapshot → s.hallX, ids, money stripped for a role without the view, mapped / unmapped commands. Names prefixed [HSYNC].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/hsync-entry.ts',"export {hallFromServer,hall} from '../../src/clients/sync/hall';\nexport {AREAS} from '../../src/clients/sync/registry';");
  await build({entryPoints:['tests/.generated/hsync-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/hsync.mjs',logLevel:'error'});
  const X=await import('./.generated/hsync.mjs');
  const line=(seq,kind,rate,extra=false)=>({seq,kind,refId:null,desc:kind+' line',qty:2,unit:'each',ratePaise:rate,listRatePaise:rate,discountPaise:0,gstClass:kind==='MENU'?'BANQUET_FOOD':'HALL_RENT',taxBps:1800,extra});
  const snap={seesMoney:true,lostReasons:[{id:'LR-PRICE',label:'Price too high',needsNote:false}],
    venues:[{venueId:1,code:'V-SAGAR',name:'Sagar Hall',kind:'HALL',active:true,version:2,capacity:{ROUND:150},combinedOf:[],minPlates:null,gstClass:'HALL_RENT',slots:[{slot:'EVENING',from:'18:00',to:'23:00',ratePaise:1500000,minPlates:null}]},
      {venueId:2,code:'V-BOTH',name:'Both',kind:'HALL',active:true,version:1,capacity:{},combinedOf:[1],minPlates:null,gstClass:'HALL_RENT',slots:[]}],
    enquiries:[{enquiryId:7,name:'Test',phone:'9000000001',eventType:'WEDDING',dates:['2026-12-05'],slot:'EVENING',pax:120,venueId:1,source:'PHONE',followUp:'2026-11-01',note:null,status:'OPEN',lostReasonId:null,lostNote:null,
      softVenueId:1,softUntil:'2026-11-03',version:3,guestId:null,companyId:null,createdUtc:'2026-10-24T05:30:00Z'}],
    quotations:[],events:[{eventId:9,quotationId:4,version:1,venueId:1,eventDate:'2026-12-05',dates:['2026-12-05'],slot:'EVENING',eventType:'WEDDING',status:'BILLED',guaranteedPax:100,actualPax:110,customerName:'Test',customerPhone:'9000000001',
      companyId:null,blockId:null,invoiceNo:'HPE/2627/0001',invoiceId:55,invoiceDate:'2026-12-05',totalPaise:236000,taxablePaise:200000,cgstPaise:18000,sgstPaise:18000,advanceAppliedPaise:100000,balancePaise:136000,settleMode:'DUE',cancelDate:null,
      cancelKeptPaise:null,cancelRefundPaise:null,refundState:null,cancelReason:null,billedPax:110,lines:[line(1,'HALL',100000),line(2,'EXTRA',0,true)],advance:[{seq:1,label:'Advance',pctBps:2500,amountPaise:59000,due:'ON_ACCEPT',paidPaise:59000}],
      receipts:[{receiptId:3,date:'2026-10-24',kind:'ADVANCE',tender:'UPI',companyId:null,udhaar:false,reference:'U1',amountPaise:100000,atUtc:'2026-10-24T06:00:00Z'}],createdUtc:'2026-10-24T05:30:00Z'}]};
  test('[HSYNC] snapshot → hallX: venue ids are codes, ids ENQ-/HEV-, combined hall by code, soft hold date, invoice figures, extras apart from lines',()=>{
    const h=X.hallFromServer(snap);
    assert.deepEqual(h.venues.map(v=>v.id),['V-SAGAR','V-BOTH']);assert.deepEqual(h.venues[1].combinedOf,['V-SAGAR']);assert.equal(h.venues[0].slots[0].ratePaise,1500000);
    const q=h.enquiries[0];assert.equal(q.id,'ENQ-7');assert.equal(q.venueId,'V-SAGAR');assert.equal(q.softHold.venueId,'V-SAGAR');assert.equal(q.softHold.until.slice(0,10),'2026-11-03');
    const e=h.events[0];assert.equal(e.id,'HEV-9');assert.equal(e.lines.length,1);assert.equal(e.extras.length,1);assert.equal(e.invoice.no,'HPE/2627/0001');assert.equal(e.invoice.totalPaise,236000);
    assert.equal(e.receipts[0].id,'HRC-3');assert.equal(e.receipts[0].method,'UPI');assert.equal(e.advance[0].paidPaise,59000);assert.equal(e.invoice.lines.length,2);
  });
  test('[HSYNC] a snapshot without money (housekeeping) carries zero rupees and the screens still get a valid slice',()=>{
    const bare=JSON.parse(JSON.stringify(snap));for(const v of bare.venues)for(const s of v.slots)s.ratePaise=null;
    for(const e of bare.events){for(const k of ['totalPaise','taxablePaise','cgstPaise','sgstPaise','advanceAppliedPaise','balancePaise'])e[k]=null;e.invoiceNo=null;e.invoiceId=null;e.receipts=[];e.advance=[];for(const l of e.lines){l.ratePaise=null;l.listRatePaise=null;l.discountPaise=null;}}
    const h=X.hallFromServer(bare);assert.equal(h.events[0].invoice,undefined);assert.equal(h.venues[0].slots[0].ratePaise,0);assert.equal(h.events[0].lines[0].ratePaise,0);
  });
  test('[HSYNC] area: registered once, owns hallX, maps the money commands and lists H2 / groups commands as unmapped with a reason',()=>{
    assert.equal(X.AREAS.filter(a=>a.id==='hall').length,1);
    const mapped=X.hall.commands.map(c=>c.type);
    for(const t of ['HALL_ENQUIRY_ADD','HALL_ENQUIRY_UPDATE','HALL_ENQUIRY_LOST','HALL_ENQUIRY_SOFT_HOLD','HALL_VENUE_UPSERT','QUO_ACCEPT','HALL_ADVANCE_RECEIVE','HALL_EVENT_CLOSE','HALL_EVENT_CANCEL','HALL_REFUND','HALL_CREDIT_NOTE','QUO_CREATE','QUO_EDIT','QUO_REVISE','QUO_SEND','QUO_LOST','QUO_CANCEL','QUO_PLACEHOLDER_OVERRIDE','QUO_DISCOUNT_APPROVE','HALL_TNC_TEMPLATE_SAVE'])assert.ok(mapped.includes(t),t);
    for(const t of ['HALL_ROOM_BLOCK','HALL_NIGHT_RUN'])assert.ok(X.hall.unmapped[t]?.length>10,t);
    assert.equal(X.hall.loaders[0].slice,'hallX');
  });
}
