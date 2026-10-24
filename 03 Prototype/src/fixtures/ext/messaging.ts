// Module 24–25 · Notifications & document capture — fixture extensions (fictional data only).
// Sample documents are fictional; ID numbers exist ONLY in masked form (no full ID number anywhere).
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {MSG_DEFAULT_TEMPLATES,MSG_DEFAULT_RULES,msgTriggerEvents,msgRender,msgValues,msgRecipient,type MSGMessage,type MSGSample,type MSGChannel} from '../../domain/streams/messaging';

/** Fictional sample documents for simulated capture ("Extract" returns these fields). */
export const msgSampleDocs:MSGSample[]=[
  {id:'S-ID-1',kind:'id-card',title:'Aadhaar-style card (demo)',file:'demo-id-aditi.jpg',hint:'Aditi Nair · arriving today',fields:[
    {key:'name',label:'Name',value:'Aditi Nair',confidence:0.97},
    {key:'idType',label:'ID type',value:'Aadhaar (demo)',confidence:0.95},
    {key:'idMasked',label:'ID number (masked)',value:'XXXX XXXX 7342',confidence:0.93,locked:true},
    {key:'idLast4',label:'ID last 4',value:'7342',confidence:0.93},
    {key:'city',label:'City',value:'Kochi, Kerala',confidence:0.88}]},
  {id:'S-ID-2',kind:'id-card',title:'Passport-style page (demo)',file:'demo-passport-ishaan.jpg',hint:'Ishaan Bose · arriving today',fields:[
    {key:'name',label:'Name',value:'Ishaan Bose',confidence:0.91},
    {key:'idType',label:'ID type',value:'Passport (demo)',confidence:0.96},
    {key:'idMasked',label:'ID number (masked)',value:'X•••4Z19',confidence:0.72,locked:true},
    {key:'idLast4',label:'ID last 4',value:'4Z19',confidence:0.62},
    {key:'city',label:'Place of issue',value:'Kolkata',confidence:0.84}]},
  {id:'S-BILL-1',kind:'vendor-bill',title:'Dairy bill (demo)',file:'demo-bill-dairy.jpg',hint:'Milk and curd for breakfast',fields:[
    {key:'vendor',label:'Vendor',value:'Shree Ganesh Dairy (fictional)',confidence:0.94},
    {key:'billNo',label:'Bill no.',value:'SGD/1182',confidence:0.89},
    {key:'billDate',label:'Bill date',value:'2026-10-24',confidence:0.92},
    {key:'amount',label:'Amount (₹)',value:'1240.00',confidence:0.86},
    {key:'category',label:'Category',value:'Supplies',confidence:0.81}]},
  {id:'S-BILL-2',kind:'vendor-bill',title:'Electrician invoice (demo)',file:'demo-bill-electrical.jpg',hint:'Corridor light repair',fields:[
    {key:'vendor',label:'Vendor',value:'Kaveri Electricals (fictional)',confidence:0.9},
    {key:'billNo',label:'Bill no.',value:'KE-0457',confidence:0.77},
    {key:'billDate',label:'Bill date',value:'2026-10-24',confidence:0.88},
    {key:'amount',label:'Amount (₹)',value:'3850.00',confidence:0.68},
    {key:'category',label:'Category',value:'Maintenance',confidence:0.83}]},
  {id:'S-LTR-1',kind:'company-letter',title:'Company billing letter (demo)',file:'demo-letter-nair-logistics.pdf',hint:'Aditi Nair · company billing',fields:[
    {key:'company',label:'Company',value:'Nair Demo Logistics Pvt Ltd',confidence:0.93},
    {key:'gstin',label:'GSTIN',value:'32ABCDE4321K1Z2',confidence:0.74},
    {key:'guest',label:'Guest named',value:'Aditi Nair',confidence:0.95},
    {key:'stay',label:'Stay',value:'24–26 Oct 2026',confidence:0.9},
    {key:'billing',label:'Billing instruction',value:'Room + breakfast to company; extras paid direct',confidence:0.85}]},
];

/** Called on every new base fixture (all scenarios start from it). */
export function extendBase(s:State):void{
  // Events of the seeded bookings count as already handled, except today's pre-arrival notes (shown as pending triggers).
  const fired=msgTriggerEvents(s,MSG_DEFAULT_RULES).filter(t=>t.rule.id!=='R2').map(t=>t.key);
  const outbox:MSGMessage[]=[];
  const add=(bid:string,tpl:MSGMessage['template'],channel:MSGChannel,queuedAt:string,outcome:MSGMessage['outcome'],rule?:string,blocked?:string)=>{
    const b=s.bookings.find(b=>b.id===bid);if(!b)return;const t=MSG_DEFAULT_TEMPLATES.find(t=>t.id===tpl)!;const v=msgValues(s,b);
    outbox.push({id:'MSG-'+(outbox.length+1),template:tpl,channel,booking:b.id,guestKey:b.guestId||'B:'+b.id,guest:b.guest,to:msgRecipient(s,b,channel).masked,
      subject:channel==='Email'?msgRender(t.subject,v).text:undefined,body:msgRender(t.body,v).text,queuedAt,outcome,actor:'reception',...(rule?{rule}:{}),...(blocked?{blocked}:{})});
  };
  add('HCC-1049','booking-confirmation','WhatsApp','2026-10-22T11:05','Read','R1');
  add('HCC-1050','booking-confirmation','WhatsApp','2026-10-22T16:40','Delivered','R1');
  add('HCC-1041','checkin-welcome','WhatsApp','2026-10-23T14:20','Read','R3');
  add('HCC-1043','payment-receipt','SMS','2026-10-23T14:45','Failed','R4');
  add('HCC-1046','checkin-welcome','WhatsApp','2026-10-23T15:10','Read','R3','Guest opted out of messages');
  add('HCC-1047','checkin-welcome','SMS','2026-10-23T16:05','Delivered','R3');
  add('HCC-1042','payment-receipt','Email','2026-10-24T07:50','Delivered','R4');
  // Remove undefined subject keys so the state survives a JSON round-trip unchanged.
  outbox.forEach(m=>{if(m.subject===undefined)delete m.subject;});
  s.messagingX={...(s.messagingX||{}),
    templates:MSG_DEFAULT_TEMPLATES.map(t=>({...t})),rules:MSG_DEFAULT_RULES.map(r=>({...r})),
    consent:{
      'G-0006':{whatsapp:true,sms:true,email:true,optOut:true,updatedAt:'2026-10-23T15:00',by:'reception'},
      'G-0007':{whatsapp:false,sms:true,email:true,optOut:false,updatedAt:'2026-10-23T16:00',by:'reception'}},
    outbox,fired,docs:[]};
}
export const scenarioPatches:Record<string,(s:State)=>void>={
  S1:s=>{s.messagingX={...(s.messagingX||{}),outbox:[],fired:[],docs:[],consent:{}};},
};
export const scenarioInfo:ScenarioInfo[]=[];
