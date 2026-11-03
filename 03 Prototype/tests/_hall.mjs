// F6 H1 test helpers: quotations are created by H2's QUO_CREATE / QUO_SEND; until then the H1 tests place a SENT quotation in s.hallX directly.
export function hallFixtures(M){
const addDays=(d,n)=>new Date(Date.parse(d+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);
  const SILVER=65000,HALL=1500000,DECOR=1000000;
  const line=(id,kind,desc,qty,unit,list,rate,gstClass,refId)=>({id,kind,desc,qty,unit,listRatePaise:list,ratePaise:rate,discountPaise:0,gstClass,...(refId?{refId}:{})});
  /** v1 list rates; v2 = 5 % Manager discount as lower rates (spec §21). */
  const goldenLines=(pct=0)=>{const r=x=>x-Math.round(x*pct/100);return [line('L1','HALL','Sagar Hall · evening',1,'slot',HALL,r(HALL),'HALL_RENT','V-SAGAR'),
    line('L2','MENU','Silver Veg per plate',150,'plate',SILVER,r(SILVER),'BANQUET_FOOD','MP-SILVER-VEG'),line('L3','EXTRA','Stage decor',1,'lot',DECOR,r(DECOR),'SERVICE')];};
  let n=0;
  /** Adds an enquiry (command) and a SENT quotation for it (direct, as H2 would), returns [state, quotationId, enquiryId]. */
  function quote(s,{venueId='V-SAGAR',dates=['2026-12-05'],slot='EVENING',pax=150,guaranteedPax=150,lines=goldenLines(5),validUntil,status='SENT',setup='ROUND',versions}={}){
    s=M.execute(s,'reception',{type:'HALL_ENQUIRY_ADD',name:'Kulkarni family (fictional)',phone:'9000000123',eventType:'RECEPTION',dates,slot,pax,venueId,source:'PHONE',followUp:s.date});
    const x=s.hallX,enq=x.enquiries.at(-1);n++;
    const v=(vn,st)=>({v:vn,status:st,header:{eventType:'RECEPTION',dates,slot,venueId,setup,pax,guaranteedPax},lines,advance:[],tnc:{templateId:'T-GEN',v:1,clauses:[],hasPlaceholder:false},
      validUntil:validUntil??addDays(s.date,15),createdBy:'manager',createdAt:s.clock});
    const vs=versions??[v(1,status)];
    const q={id:'QUO-T'+n,number:`QT/2627/${String(n).padStart(4,'0')}`,fy:'2627',enquiryId:enq.id,versions:vs,currentVersion:vs.at(-1).v,customer:{name:enq.name,phone:enq.phone,company:'CO-1'}};
    s={...s,hallX:{...x,quotations:[...x.quotations,q],enquiries:x.enquiries.map(e=>e.id===enq.id?{...e,status:'QUOTED'}:e)}};
    return [s,q.id,enq.id];
  }
  const at=(s,date)=>({...s,date,clock:date+'T10:00'});
  /** Σ debit − credit per account over the given dates (hall vouchers only). */
  function ledger(s,dates){const t={};for(const d of dates)for(const v of M.accLiveVouchers(s,d).filter(v=>v.source.kind==='hall'))for(const e of v.entries)t[e.account]=(t[e.account]??0)+e.debit-e.credit;return t;}
  return {goldenLines,quote,at,ledger,line};
}
