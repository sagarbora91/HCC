import React from 'react';
import {RotateCcw,ArrowRight,Info,Layers} from 'lucide-react';
import {scenarios,money,drawer} from '../../services/mock';
import {useHcc} from '../context';
import {Pill} from '../../components/ui';
export function ScenarioModal(){
  const {s,loadScenario}=useHcc();
  return <><div className="modal-heading"><Layers size={25}/><h2>Choose a scenario</h2><p>Loading a scenario replaces all demo data in this browser with that fixture. Fixture keys (rooms, bookings, users) are the same in every scenario.</p></div>
    <div className="scenario-list">{scenarios.map(x=><div className={'scenario '+(x.id===s.scenario?'current':'')} key={x.id}>
      <div><strong>{x.id} · {x.name}</strong><p>{x.purpose}</p>{!x.available&&<small>Available from {x.availableFrom}</small>}</div>
      {x.id===s.scenario&&<Pill tone="green">Loaded</Pill>}
      <button className="btn small-btn" disabled={!x.available} onClick={()=>loadScenario(x.id)}>{x.available?'Load':'Not yet built'}</button>
    </div>)}</div></>;
}
export function ResetModal(){
  const {s,closeModal,resetScenario}=useHcc();
  return <><div className="modal-heading"><RotateCcw size={25}/><h2>Reset your demo?</h2><p>This removes edits in this browser and reloads scenario <strong>{s.scenario}</strong> from its fixture. Your source files are unaffected.</p></div>
    <div className="modal-actions"><button className="btn" onClick={closeModal}>Keep exploring</button><button className="btn danger" onClick={resetScenario}>Reset demo</button></div></>;
}
// Walkthrough (BL-017): the 12-step, three-day S0 script from HCC_Fixture_and_Scenario_Catalog.md §4 · owned by stream F.
export type GuideStep={n:string;date:string;actor:string;page:string;title:string;actions:string;expected:string};
export const guideSteps:GuideStep[]=[
  {n:'1',date:'24 Oct',actor:'owner',page:'overview',title:'Review the hotel',actions:'Open Overview; look at the room board and cash card.',expected:'8 occupied · 23 sellable · drawer ₹10,200 · outstanding ₹1,950'},
  {n:'2',date:'24 Oct',actor:'reception',page:'reservations',title:'Reserve Room 105',actions:'Book Room 105, 24→26 Oct, 2 guests, source Phone, guest “Rahul Verma (fictional)”. Record a ₹1,000 UPI advance, reference DEMO-UPI-0001.',expected:'Booking Confirmed · folio balance −₹1,000 · drawer unchanged'},
  {n:'3',date:'24 Oct',actor:'reception',page:'folios',title:'Register and check in',actions:'Open the Room 105 folio, verify the fictional registration and check in.',expected:'Room 105 Occupied · arrivals list updated · audit entries'},
  {n:'3b',date:'24 Oct',actor:'cashier',page:'folios',title:'Take the deposit',actions:'Record a ₹500 refundable deposit in cash on the Room 105 folio.',expected:'Deposit held ₹500 · balance still −₹1,000 · drawer ₹10,700'},
  {n:'4a',date:'24 Oct',actor:'manager',page:'closing',title:'Close 24 Oct',actions:'Approve EXP-102 (Expenses). Cash shift: count ₹10,700. Day closing: post room nights, close the day, open 25 Oct.',expected:'105 balance ₹1,200 · 24 Oct closed · opening float ₹10,700'},
  {n:'4b',date:'25 Oct',actor:'reception',page:'folios',title:'Service and discount',actions:'Add Laundry service ₹500; request a ₹500 discount “Service delay goodwill”.',expected:'Discount above ₹250 → approval pending · balance ₹1,750'},
  {n:'5',date:'25 Oct',actor:'manager',page:'expenses',title:'Approve the discount',actions:'Try approving as Reception (denied) and recording a payment as Housekeeper (denied); then approve as Manager.',expected:'Discount line −₹500 (tax −₹50) · balance ₹1,200'},
  {n:'5b',date:'25 Oct',actor:'manager',page:'closing',title:'Close 25 Oct',actions:'Post room nights, count the shift at ₹10,700 (cashier page), close the day, open 26 Oct.',expected:'105 balance ₹3,400 · invoice total ₹4,400 (net ₹4,000 + tax ₹400)'},
  {n:'6',date:'26 Oct',actor:'cashier',page:'folios',title:'Settle and return deposit',actions:'Settle ₹2,000 cash + ₹1,400 UPI (DEMO-UPI-0002); return the ₹500 deposit in cash.',expected:'Balance ₹0 · deposit ₹0 · drawer ₹12,200'},
  {n:'7',date:'26 Oct',actor:'reception',page:'folios',title:'Check out and print',actions:'Check out Room 105 and print the sample invoice.',expected:'Departed · Room 105 Dirty · exactly one housekeeping task (Ravi) · invoice ₹4,400'},
  {n:'8',date:'26 Oct',actor:'housekeeper',page:'housekeeping',title:'Clean and inspect',actions:'Ravi starts, ticks 3 items and completes; then Neha (Supervisor) inspects.',expected:'Room 105 Ready'},
  {n:'9',date:'26 Oct',actor:'cashier',page:'expenses',title:'Cash expense',actions:'Cashier records “Aqua Water Cans” ₹300, drawer cash; Manager approves and posts it.',expected:'Drawer ₹11,900'},
  {n:'10',date:'26 Oct',actor:'manager',page:'closing',title:'Close 26 Oct',actions:'Post room nights, count the shift at ₹11,900, close the day; try closing again.',expected:'Day closed · rerun refused (“closed”) · totals unchanged'},
  {n:'11',date:'26 Oct',actor:'owner',page:'reports',title:'Drill into reports',actions:'Reports → range 24–26 Oct → click receipts, tax, deposits and expenses totals down to the Room 105 records.',expected:'Room 105: ₹4,000 net / ₹400 tax · deposit in ₹500 / out ₹500 · expense ₹300'},
  {n:'12',date:'26 Oct',actor:'owner',page:'access',title:'Access policy',actions:'Remove a permission from Reception; disable Aarav; switch to Aarav and try a booking.',expected:'Next command → user disabled · audit shows the change and the denial'},
];
/** One walkthrough step as a phone card (MB-09): header, what to do, expected figures, full-width "Go there". */
export function GuideStepCard({x,onGo}:{x:GuideStep;onGo:(x:GuideStep)=>void}){
  const {s}=useHcc();const who=s.users.find(u=>u.id===x.actor)?.name.split(' · ')[0]??x.actor;
  return <li className="mo-step"><div className="mo-step-head"><span className="mo-step-no">{x.n}</span><div><h3>{x.title}</h3><small>{x.date} · as {who}</small></div></div>
    <p>{x.actions}</p><p className="mo-step-expect"><strong>Expect:</strong> {x.expected}</p>
    <button type="button" className="btn full mo-btn" onClick={()=>onGo(x)}>Go there<ArrowRight size={17}/></button></li>;
}
export function GuideModal(){
  const {s,loadScenario,switchUser,go,display}=useHcc();
  const who=(id:string)=>s.users.find(u=>u.id===id)?.name??id;
  const goThere=(x:GuideStep)=>{switchUser(x.actor);go(x.page);};
  return <><div className="modal-heading"><div className="eyebrow">WALKTHROUGH · SCENARIO S0</div><h2>One guest. Three business days.</h2><p>Room 105, 24→26 Oct 2026. Each step names the demo user and the expected figures; “Go there” switches to that user and opens the screen. All data is fictional and resettable.</p></div>
    <div className="f-guide-now" role="status"><span>Now: scenario <strong>{s.scenario}</strong> · business date <strong>{s.date}</strong> · expected drawer <strong>{money(drawer(s))}</strong></span>
      <button className={'btn small-btn'+(display.isMobile?' mo-btn':'')} onClick={()=>loadScenario('S0')}><RotateCcw size={14}/> {s.scenario==='S0'?'Restart S0':'Load S0'}</button></div>
    <p className={'muted'+(display.isMobile?' mo-guide-path':'')}>Drawer path: ₹10,200 → ₹10,700 → ₹12,200 → ₹11,900 · Room 105 invoice ₹4,400.</p>
    {display.isMobile?<ol className="mo-steps">{guideSteps.map(x=><GuideStepCard key={x.n} x={x} onGo={goThere}/>)}</ol>:
    <ol className="f-guide">{guideSteps.map(x=><li key={x.n} className="guide-step">
      <span>{x.n}</span><div><h3>{x.title} <small>· {x.date} · {who(x.actor)}</small></h3><p>{x.actions}</p><p className="f-expected"><strong>Expect:</strong> {x.expected}</p></div>
      <button className="btn small-btn" onClick={()=>goThere(x)}>Go there <ArrowRight size={14}/></button></li>)}</ol>}
    {s.scenario!=='S0'&&<p className="f-why">The expected figures assume the S0 fixture — load S0 first.</p>}
    <div className="info-note"><Info size={17}/>The 10% tax is synthetic test data. Deposits never become revenue. Room nights come from the night audit on Day closing, one per night. Try a fault in the demo {display.isMobile?'sheet (DEMO pill)':'toolbar'} (e.g. Lost response) before recording a payment, then retry — no duplicate receipt is created.</div>
    <div className="modal-actions"><button className="btn primary" onClick={()=>loadScenario('S0')}><RotateCcw size={15}/> {s.scenario==='S0'?'Restart S0 from step 1':'Load S0 and start'}</button></div></>;
}
