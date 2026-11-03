// F6-H3 · modals 'hall-print' (quotation / function sheet / invoice / receipt), 'hall-venue' (halls master, Owner), 'hall-menu' (menu package, Owner / Manager).
import React,{useState} from 'react';
import {Printer} from 'lucide-react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {printDocument} from '../../app/native';
import {ModalFrame} from '../../components/fit';
import {Empty} from '../../components/ui';
import {hallSlice,HALL_SLOTS,type HallSetup,type Venue,type MenuPackage} from '../../domain/streams/hall';
import {posSlice} from '../../domain/streams/pos';
import {POS_FIXTURE_MENU} from '../../fixtures/ext/pos';
import {money} from '../../domain/money';
import {hallUi} from './access';
import {hallQuoteDoc,hallBeo,hallInvoiceDoc,hallReceiptDoc,SLOT_LABEL,SETUP_LABEL} from './logic';
import {QuotationDoc,BeoDoc,InvoiceDoc,ReceiptDoc} from './prints';
import {Field,RupeeInput} from './ui';

export function HallPrintModal({id}:ModalProps){
  const {s,uid,closeModal}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s);const [kind,a,b]=(id??'').split(':');
  const [prices,setPrices]=useState(false);
  if(!ui.view)return <Empty title="Not available"/>;
  let doc:React.ReactNode=null,title='Print';
  if(kind==='quote'&&ui.seesMoney){const q=x.quotations.find(o=>o.id===a),v=q?.versions.find(o=>o.v===Number(b)||(!b&&o.v===q.currentVersion));if(q&&v){doc=<QuotationDoc m={hallQuoteDoc(s,q,v)}/>;title=`Quotation ${q.number}`;}}
  const e=x.events.find(o=>o.id===a);
  if(kind==='beo'&&e){doc=<BeoDoc m={hallBeo(s,e,ui.seesMoney&&prices)}/>;title=`Function sheet ${e.id}`;}
  if(kind==='invoice'&&e&&ui.seesMoney){const m=hallInvoiceDoc(s,e);if(m){doc=<InvoiceDoc m={m}/>;title=`Invoice ${m.no}`;}}
  if(kind==='receipt'&&e&&ui.seesMoney){const r=e.receipts?.find(o=>o.id===b);if(r){doc=<ReceiptDoc m={hallReceiptDoc(s,e,r)}/>;title=`Receipt ${r.id}`;}}
  if(!doc)return <Empty title="Nothing to print here"/>;
  return <ModalFrame heading={<h2>{title}</h2>} actions={<><button type="button" className="btn" onClick={closeModal}>Close</button>
    <button type="button" className="btn primary" onClick={()=>{void printDocument();}}><Printer size={15}/> Print</button></>}>
    {kind==='beo'&&ui.seesMoney&&<label className="inline no-print"><input type="checkbox" checked={prices} onChange={ev=>setPrices(ev.target.checked)}/> Include prices (desk copy; the kitchen copy has none)</label>}
    {doc}</ModalFrame>;
}

export function HallVenueModal({id}:ModalProps){
  const {s,uid,run,busy,closeModal}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s),v=id?x.venues.find(o=>o.id===id):undefined;
  if(!ui.venues)return <Empty title="Not available" body="Only the Owner edits the halls."/>;
  return <VenueForm key={v?.id??'new'} v={v}/>;
}
function VenueForm({v}:{v?:Venue}){
  const {s,run,busy,closeModal}=useHcc();const x=hallSlice(s);
  const [f,setF]=useState({name:v?.name??'',kind:(v?.kind??'HALL') as Venue['kind'],active:v?.active??true,combined:v?.combinedOf??[] as string[],minPlates:String(v?.minPlates??'')});
  const [cap,setCap]=useState<Record<string,string>>(Object.fromEntries(Object.entries(v?.capacity??{}).map(([k,n])=>[k,String(n)])));
  const [slots,setSlots]=useState(HALL_SLOTS.map(sl=>{const o=v?.slots.find(z=>z.slot===sl);return {slot:sl,on:!!o||(!v&&sl==='EVENING'),from:o?.from??'18:00',to:o?.to??'23:00',rate:o?.ratePaise??0};}));
  const singles=x.venues.filter(o=>!o.combinedOf&&o.id!==v?.id);
  const save=()=>run({type:'HALL_VENUE_UPSERT',version:v?.version,venue:{id:v?.id,name:f.name,kind:f.kind,active:f.active,combinedOf:f.combined.length?f.combined:undefined,minPlates:Number(f.minPlates)||undefined,
    capacity:Object.fromEntries(Object.entries(cap).filter(([,n])=>Number(n)>0).map(([k,n])=>[k,Number(n)])),
    slots:slots.filter(o=>o.on).map(o=>({slot:o.slot,from:o.from,to:o.to,ratePaise:o.rate}))}},v?'Hall updated':'Hall added',{close:true});
  return <ModalFrame heading={<><h2>{v?`Hall: ${v.name}`:'New hall'}</h2><p>Rates here are the list rates a quotation starts from.</p></>}
    actions={<><button type="button" className="btn" onClick={closeModal}>Close</button><button type="submit" form="hall-venue-form" className="btn primary" disabled={busy||f.name.trim().length<2}>Save</button></>}>
    <form id="hall-venue-form" className="form hall-form" onSubmit={e=>{e.preventDefault();void save();}}>
      <div className="hall-fields"><Field label="Name"><input value={f.name} maxLength={60} onChange={e=>setF(o=>({...o,name:e.target.value}))}/></Field>
        <Field label="Kind"><select value={f.kind} onChange={e=>setF(o=>({...o,kind:e.target.value as Venue['kind']}))}><option value="HALL">Hall</option><option value="LAWN">Lawn</option><option value="TERRACE">Terrace</option></select></Field>
        <Field label="Minimum plates"><input inputMode="numeric" value={f.minPlates} onChange={e=>setF(o=>({...o,minPlates:e.target.value.replace(/\D/g,'')}))}/></Field>
        <Field label="In use"><select value={f.active?'1':'0'} onChange={e=>setF(o=>({...o,active:e.target.value==='1'}))}><option value="1">Active</option><option value="0">Retired</option></select></Field></div>
      <fieldset className="hall-box"><legend>Seats by set-up</legend><div className="hall-fields">{(Object.keys(SETUP_LABEL) as HallSetup[]).map(k=><Field key={k} label={SETUP_LABEL[k]}><input inputMode="numeric" value={cap[k]??''} onChange={e=>setCap(o=>({...o,[k]:e.target.value.replace(/\D/g,'')}))}/></Field>)}</div></fieldset>
      <fieldset className="hall-box"><legend>Parts of the day and list rate</legend>{slots.map((o,i)=><div className="inline hall-add" key={o.slot}>
        <label className="inline"><input type="checkbox" checked={o.on} onChange={e=>setSlots(l=>l.map((z,j)=>j===i?{...z,on:e.target.checked}:z))}/> {SLOT_LABEL[o.slot]}</label>
        <Field label="From"><input type="time" value={o.from} disabled={!o.on} onChange={e=>setSlots(l=>l.map((z,j)=>j===i?{...z,from:e.target.value}:z))}/></Field>
        <Field label="To"><input type="time" value={o.to} disabled={!o.on} onChange={e=>setSlots(l=>l.map((z,j)=>j===i?{...z,to:e.target.value}:z))}/></Field>
        <Field label="Rate (₹)"><RupeeInput label={`${SLOT_LABEL[o.slot]} rate`} disabled={!o.on} value={o.rate} onChange={p=>setSlots(l=>l.map((z,j)=>j===i?{...z,rate:p}:z))}/></Field></div>)}</fieldset>
      {singles.length>=2&&<fieldset className="hall-box"><legend>Combined hall (booking it blocks every hall in it)</legend><div className="inline">{singles.map(o=><label key={o.id} className="inline"><input type="checkbox" checked={f.combined.includes(o.id)}
        onChange={e=>setF(z=>({...z,combined:e.target.checked?[...z.combined,o.id]:z.combined.filter(c=>c!==o.id)}))}/> {o.name}</label>)}</div></fieldset>}
    </form></ModalFrame>;
}

export function HallMenuModal({id}:ModalProps){
  const {s,uid}=useHcc();const ui=hallUi(s,uid),m=id?hallSlice(s).menus.find(o=>o.id===id):undefined;
  if(!ui.menus)return <Empty title="Not available" body="Menus are kept by the Owner and Manager."/>;
  return <MenuForm key={m?.id??'new'} m={m}/>;
}
function MenuForm({m}:{m?:MenuPackage}){
  const {s,run,busy,closeModal}=useHcc();
  const pos=posSlice(s).menu.length?posSlice(s).menu:POS_FIXTURE_MENU;
  const [f,setF]=useState({name:m?.name??'',price:m?.perPlatePaise??0,veg:m?.veg??true,active:m?.active??true,items:m?.posItemIds??[] as string[]});
  return <ModalFrame heading={<><h2>{m?`Menu: ${m.name}`:'New menu package'}</h2><p>One price per plate; the items are the kitchen’s list (from the POS menu).</p></>}
    actions={<><button type="button" className="btn" onClick={closeModal}>Close</button><button type="submit" form="hall-menu-form" className="btn primary" disabled={busy||f.name.trim().length<2||f.price<=0||!f.items.length}>Save</button></>}>
    <form id="hall-menu-form" className="form hall-form" onSubmit={e=>{e.preventDefault();void run({type:'HALL_MENU_UPSERT',menu:{id:m?.id,name:f.name,perPlatePaise:f.price,veg:f.veg,active:f.active,posItemIds:f.items}},m?'Menu updated':'Menu added',{close:true});}}>
      <div className="hall-fields"><Field label="Name"><input value={f.name} maxLength={60} onChange={e=>setF(o=>({...o,name:e.target.value}))}/></Field>
        <Field label="Price per plate (₹)"><RupeeInput label="Price per plate" value={f.price} onChange={p=>setF(o=>({...o,price:p}))}/></Field>
        <Field label="Veg only"><select value={f.veg?'1':'0'} onChange={e=>setF(o=>({...o,veg:e.target.value==='1'}))}><option value="1">Veg</option><option value="0">Veg and non-veg</option></select></Field>
        <Field label="In use"><select value={f.active?'1':'0'} onChange={e=>setF(o=>({...o,active:e.target.value==='1'}))}><option value="1">Active</option><option value="0">Retired</option></select></Field></div>
      <fieldset className="hall-box"><legend>Items ({f.items.length})</legend><div className="hall-items" data-fit-scroll="" tabIndex={0} aria-label="POS menu items">{pos.map(i=><label key={i.id} className="inline"><input type="checkbox" checked={f.items.includes(i.id)}
        onChange={e=>setF(o=>({...o,items:e.target.checked?[...o.items,i.id]:o.items.filter(z=>z!==i.id)}))}/> {i.name} <small className="muted">{i.category} · {money(i.price)}</small></label>)}</div></fieldset>
    </form></ModalFrame>;
}
