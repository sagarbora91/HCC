// LF-images (LF-CONTRACT §1.11 G1, §10.2 B6): the shared photo compressor (PHOTO_DOC 150 KB / PHOTO_SLIP 100 KB; the slip search byte-for-byte as before),
// the cap messages, and the Owner's invoice title / notes / footer on every document type. Names prefixed [LF-IMAGES].
// Party photos and per-member IDs (B5): the server schema (4707) and the object kind exist; the capture screens and the member capture command are listed
// in LF-docdb-DONE.md as not built in this lane.
import {build} from 'esbuild';
import {mkdir} from 'node:fs/promises';
await mkdir('tests/.generated',{recursive:true});
await build({entryPoints:['src/app/slipPhoto.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/slipphoto-lf.mjs',logLevel:'error'});
const P=await import('./.generated/slipphoto-lf.mjs');

export function register({test,M,assert}){
  const {seed,execute,printNotesFor,cfgDef,DOC_MAX_BYTES,DOC_CAP_MESSAGE}=M;
  const set=(s,uid,setting,value)=>execute(s,uid,{type:'SETTING_SET',setting,value});
  /** A fake encoder: bytes = a fixed cost per pixel × the quality (smaller quality → fewer bytes), so the plan has a known answer. */
  const fake=(perPixel)=>async(w,h,q)=>new Uint8Array(Math.max(1,Math.round(w*h*perPixel*q)));

  test('[LF-IMAGES] profiles: PHOTO_DOC aims at 153,600 bytes with four edges, PHOTO_SLIP is the unchanged Wave B1 profile',()=>{
    assert.deepEqual(P.PHOTO_DOC,{maxBytes:153600,edges:[1600,1280,1024,800],qualities:[0.85,0.78,0.7,0.6,0.5,0.4],label:'document'});
    assert.deepEqual(P.PHOTO_SLIP,{maxBytes:102399,edges:[1280,1024,800],qualities:[0.82,0.72,0.62,0.52,0.42,0.32],label:'deposit slip'});
    assert.equal(P.PHOTO_DOC.maxBytes,DOC_MAX_BYTES);assert.equal(P.SLIP_MAX_BYTES,102399);
    assert.equal(typeof P.compressPhoto,'function');assert.equal(typeof P.compressSlipPhoto,'function');assert.equal(typeof P.compressBillPhoto,'function');assert.equal(typeof P.compressDocPhoto,'function');});

  test('[LF-IMAGES] the plan keeps the largest edge that fits ≤ 153,600 bytes and the highest quality at that edge',async()=>{
    // 4000 × 3000 photo; 0.05 byte per pixel at quality 1: edge 1600 → 1600×1200 = 1.92 M px → 96,000·q bytes: fits at the first quality (0.85 → 81,600).
    const a=await P.photoCompressPlan(4000,3000,fake(0.05),P.PHOTO_DOC);
    assert.deepEqual([a.width,a.height,a.quality],[1600,1200,0.85]);assert.ok(a.bytes.length<=153600);
    // 0.2 byte per pixel: 1600 edge needs 384,000·q: even 0.4 gives 153,600 + (q 0.4 → 153,600 exactly fits); the first quality that fits at 1600 is 0.4.
    const b=await P.photoCompressPlan(4000,3000,fake(0.2),P.PHOTO_DOC);
    assert.deepEqual([b.width,b.height,b.quality,b.bytes.length],[1600,1200,0.4,153600]);
    // 0.4 byte per pixel: nothing fits at 1600 (768,000·0.4 = 307,200); the next edge 1280 (1280×960 = 1,228,800 px → 491,520·q) fits at no quality either (0.4 → 196,608);
    // 1024×768 = 786,432 px → 314,573·0.4 = 125,829 fits at 0.5? 157,286 no; at 0.4 yes.
    const c=await P.photoCompressPlan(4000,3000,fake(0.4),P.PHOTO_DOC);
    assert.deepEqual([c.width,c.height,c.quality],[1024,768,0.4]);assert.ok(c.bytes.length<=153600);
    // A small photo (800×600) gives the same size at every edge: one pass over the qualities.
    let calls=0;const small=await P.photoCompressPlan(800,600,async(w,h,q)=>{calls++;return new Uint8Array(q>=0.85?200000:100000);},P.PHOTO_DOC);
    assert.deepEqual([small.width,small.height,small.quality],[800,600,0.78]);assert.equal(calls,2);});

  test('[LF-IMAGES] nothing fits → the photo is refused with a take-it-again message; an unreadable photo too',async()=>{
    await assert.rejects(P.photoCompressPlan(4000,3000,fake(5),P.PHOTO_DOC),e=>/cannot be made smaller than 150 KB\. Take the photo again, closer and in good light\./.test(e.message));
    await assert.rejects(P.photoCompressPlan(0,0,fake(1),P.PHOTO_DOC),/could not be read/);
    await assert.rejects(P.photoCompressPlan(4000,3000,async()=>new Uint8Array(0),P.PHOTO_DOC),/cannot be made smaller/);});

  test('[LF-IMAGES] the slip search is unchanged: slipCompressPlan = photoCompressPlan with PHOTO_SLIP, and its message and limit are the Wave B1 ones',async()=>{
    for(const per of [0.03,0.08,0.2]){
      const x=await P.slipCompressPlan(4000,3000,fake(per)),y=await P.photoCompressPlan(4000,3000,fake(per),P.PHOTO_SLIP);
      assert.deepEqual([x.width,x.height,x.quality,x.bytes.length],[y.width,y.height,y.quality,y.bytes.length]);assert.ok(x.bytes.length<=102399);}
    await assert.rejects(P.slipCompressPlan(4000,3000,fake(5)),e=>e.message==='The slip photo cannot be made smaller than 100 KB. Take it again, closer to the slip and in good light.');
    const s1=await P.slipCompressPlan(4000,3000,fake(0.2),150000);assert.ok(s1.bytes.length<=150000,'the limit argument still works');});

  test('[LF-IMAGES] the SCC bill constants stay exported (their tests pin them) and the pick refusal for a file above 4 MB stays',()=>{
    assert.deepEqual([P.BILL_WIDTH,P.BILL_QUALITY,P.BILL_MAX_INPUT_BYTES],[1000,0.7,4*1024*1024]);
    assert.deepEqual(P.billFit(4000,3000),{width:1000,height:750});assert.equal(P.billPickRefusal({size:4*1024*1024+1,type:'image/jpeg'}),'Photo too large (max 4MB)');
    assert.equal(P.billPickRefusal({size:100,type:'text/html'}),'Choose a photo of the bill.');assert.equal(DOC_CAP_MESSAGE,'Take the photo again, closer and in good light.');});

  test('[LF-IMAGES] B6 settings: five keys in Property and branding with the contract limits and defaults; Owner and Tech admin may set them, Reception may not',()=>{
    const d=k=>cfgDef(seed(),k);
    assert.ok(['brand.invoiceTitle','brand.invoiceSubtitle','brand.invoiceNotes','brand.receiptFooter','brand.creditNoteNotes'].every(k=>d(k).group==='property'&&!d(k).money));
    assert.deepEqual(['brand.invoiceTitle','brand.invoiceSubtitle','brand.invoiceNotes','brand.receiptFooter','brand.creditNoteNotes'].map(k=>[d(k).maxLength,d(k).default]),[[60,'Tax invoice'],[120,''],[600,''],[120,''],[300,'']]);
    let s=seed();
    s=set(s,'owner','brand.invoiceNotes','Check-out 11:00\nSubject to Latur jurisdiction');s=set(s,'techadmin','brand.invoiceTitle','Tax Invoice (GST)');
    assert.throws(()=>set(s,'owner','brand.invoiceNotes','x'.repeat(601)),/at most 600/);assert.throws(()=>set(s,'owner','brand.invoiceTitle','x'.repeat(61)),/at most 60/);
    assert.throws(()=>set(s,'reception','brand.invoiceNotes','nope'),e=>e.code==='FORBIDDEN'||/Access denied|not allowed|permission/i.test(e.message));});

  test('[LF-IMAGES] print notes: title, subtitle and notes on invoices, counter bills and POS invoices; credit-note notes on credit notes; receipt footer falls back to the invoice footer',()=>{
    let s=seed();
    const none=printNotesFor(s,'invoice');assert.deepEqual([none.title,none.subtitle,none.notes],['Tax invoice','','']);
    s=set(s,'owner','brand.invoiceSubtitle','FSSAI 11522000000123');s=set(s,'owner','brand.invoiceNotes','Check-out 11:00\nSubject to Latur jurisdiction');
    s=set(s,'owner','brand.creditNoteNotes','Refund within 7 days');s=set(s,'owner','brand.invoiceFooter','Thank you.');
    for(const doc of ['invoice','counter-bill','pos-invoice']){const t=printNotesFor(s,doc);assert.deepEqual([t.title,t.subtitle,t.notes,t.footer],['Tax invoice','FSSAI 11522000000123','Check-out 11:00\nSubject to Latur jurisdiction','Thank you.'],doc);}
    const cn=printNotesFor(s,'credit-note');assert.deepEqual([cn.title,cn.notes,cn.footer],['','Refund within 7 days','Thank you.']);
    assert.equal(printNotesFor(s,'receipt').footer,'Thank you.');assert.deepEqual([printNotesFor(s,'receipt').title,printNotesFor(s,'receipt').notes],['','']);
    s=set(s,'owner','brand.receiptFooter','Receipt — keep for your records');assert.equal(printNotesFor(s,'receipt').footer,'Receipt — keep for your records');assert.equal(printNotesFor(s,'invoice').footer,'Thank you.');});
}
