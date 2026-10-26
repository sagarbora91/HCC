// MA-9 · ONE place for every master's seed rows (plan HCC_Customisation_Features_Plan_v1.md Part 2 §9.6).
//
// A master's rows live in s.mastersX.rows[id] after the first saved change; until then (BASE, FY26, old saves) the rows are this seed, read
// by each spec's `seed` hook (domain/streams/*-master*.ts, catalogue.ts, hkmasters.ts, gsvmasters.ts …). Every value here reproduces what the
// prototype hard-coded or derived before the masters wave (rooms' floors and types, free-text vendors, designations of the staff master,
// today's payment methods, the booking sources the forms offered …), so no BASE / FY26 figure moves: BASE is never edited and the FY26
// determinism hash does not read these rows (tests/stream-masters-fixture.mjs pins both).
//
// Rules for this file
//  - PURE LEAF: `import type` only. The domain imports it (never the reverse), so it can sit under the leaf selectors (money-master.ts).
//  - Every seed row is active (a retired row is never seeded), has a unique code and a unique, ascending sortOrder.
//  - Demo data is marked: rows that exist only to make the prototype look lived-in carry `demo:true` and sit behind ONE switch,
//    MASTERS_SEED_OPTIONS (below). Today that is the example holiday calendar — DEC-083 and both servers seed no holidays.
//  - Masters whose rows ARE the fixture (rooms, outlets, tables, menu items) have no seed: their rows are read from the state slice
//    that holds them (MASTER_FIXTURE_BACKED).
import type {State,Role,Room} from '../../domain/types';
import type {MasterRow,MstDatedAmount} from '../../domain/streams/masters';
import type {RmsTypeRow,RmsOccRow} from '../../domain/streams/roommasters';
import type {CatMealPlan,CatExtra,CatDiscountReason} from '../../domain/streams/catalogue';
import type {MnyAccount,MnyMethod,MnyCategory,MnyVendor} from '../../domain/streams/money-master';
import type {FDMSourceRow,FDMIdTypeRow,FDMSeriesRow,FDMPolicyRow} from '../../domain/streams/frontdesk-master';
import type {HRDesignation,HRPayRule,HRHolidayKind} from '../../domain/streams/people-masters';
import type {HktRow,HkzRow} from '../../domain/streams/hkmasters';
import type {GcatRow,LfcRow,GSVDept} from '../../domain/streams/services';

// ───────────────────────────── Switches ─────────────────────────────
/** DEMO DATA SWITCH (one place). demoHolidays: seed the illustrative holiday calendar (8 rows, 6 paid in FY 2026-27) so the prototype's
 *  payroll / compliance screens show holidays. DEC-083 and the servers (Wave B 3400, MA-6 2860/3800) seed none: switch it off to start
 *  empty (the Owner enters the CA-confirmed calendar). */
export const MASTERS_SEED_OPTIONS={demoHolidays:true};
/** Test / deployment helper: change the switch; returns the previous values. */
export function setMastersSeedOptions(o:Partial<typeof MASTERS_SEED_OPTIONS>){const prev={...MASTERS_SEED_OPTIONS};Object.assign(MASTERS_SEED_OPTIONS,o);return prev;}

/** Masters whose rows are the fixture itself (state slice), not a seed: id → where the rows are read from. */
export const MASTER_FIXTURE_BACKED:Record<string,string>={rooms:'s.rooms',outlets:'s.posX.outlets',posTables:'s.posX.outlets[].tableRows / tables',menuItems:'s.posX.menu'};

// ───────────────────────────── Shared helpers ─────────────────────────────
/** Effective-from of seeded dated amounts: the start of FY 2025-26, before the FY26 history window, so every historical date resolves. */
export const SEED_FROM='2025-04-01';
const dm=(rupees:number):MstDatedAmount[]=>[{amount:rupees*100,effectiveFrom:SEED_FROM}];
const step=(i:number)=>(i+1)*10;
/** Upper-case code from free text: other characters → "-", at most 20, no trailing "-" (designations, booking sources). */
export const seedSlug=(t:string)=>t.toUpperCase().replace(/[^A-Z0-9]+/g,'-').replace(/^-+|-+$/g,'').slice(0,20).replace(/-+$/,'');

// ───────────────────────────── M1 · Floors ─────────────────────────────
/** One row per floor number used by the rooms ("Floor 1" …), code F<n>, sort order 10, 20, … (Room.floor stays a number). */
export const seedFloors=(rooms:Pick<Room,'floor'>[]):MasterRow[]=>
  [...new Set(rooms.map(r=>r.floor))].sort((a,b)=>a-b).map(n=>({id:'flr-'+n,code:'F'+n,name:'Floor '+n,sortOrder:n*10,active:true}));

// ───────────────────────────── M2–M5 · Room types, amenities, occupancy charges ─────────────────────────────
const TYPE_SEED:Record<string,{maxAdults:number;maxChildren:number;extraBed:boolean;bed:string;size:number;amenities:string[];desc:string}>={
  Classic:{maxAdults:2,maxChildren:0,extraBed:false,bed:'Queen',size:180,amenities:['WIFI','TEA'],desc:'Cosy room with a queen bed, ideal for solo travellers and couples.'},
  Deluxe:{maxAdults:2,maxChildren:1,extraBed:true,bed:'King',size:240,amenities:['VIEW','DESK'],desc:'Spacious room with a king bed and a sitting corner.'},
  Suite:{maxAdults:2,maxChildren:2,extraBed:true,bed:'King',size:420,amenities:['LIVING','BATH','BFAST'],desc:'Separate living area, ideal for families of up to four.'},
};
const TYPE_ORDER=['Classic','Deluxe','Suite'];
/** Code of a room type key ("Deluxe" → DELUXE). */
export const seedTypeCode=(k:string)=>k.toUpperCase().replace(/[^A-Z0-9-]+/g,'-').replace(/^-+|-+$/g,'').slice(0,20)||'TYPE';
/** Room types of the fixture rooms (Classic, Deluxe, Suite first, then by name); `keys` = only these (types that appeared after the first save). */
export function seedRoomTypes(rooms:Pick<Room,'type'>[],keys?:string[]):RmsTypeRow[]{
  const list=keys??[...new Set(rooms.map(r=>r.type))].sort((a,b)=>(TYPE_ORDER.indexOf(a)+1||99)-(TYPE_ORDER.indexOf(b)+1||99)||a.localeCompare(b));
  return list.map((k,i)=>{const d=TYPE_SEED[k];return {id:'rty-'+seedTypeCode(k).toLowerCase(),code:seedTypeCode(k),name:k,key:k,sortOrder:step(i),active:true,
    maxAdults:d?.maxAdults??2,maxChildren:d?.maxChildren??0,extraBedAllowed:d?.extraBed??false,...(d?{bedType:d.bed,sizeSqft:d.size,description:d.desc}:{}),amenities:d?.amenities??[]};});
}
export const SEED_AMENITIES:[string,string,string][]=[['WIFI','Wi-Fi','wifi'],['TEA','Tea & coffee','coffee'],['AC','Air conditioning','snowflake'],['TV','Television','tv'],
  ['DESK','Work desk','briefcase'],['VIEW','City view','building'],['LIVING','Living room','sofa'],['BATH','Bathtub','bath'],['BFAST','Breakfast for 2','utensils']];
export const seedAmenities=():MasterRow[]=>SEED_AMENITIES.map(([code,name,icon],i)=>({id:'amn-'+code.toLowerCase(),code,name,icon,showOnWebsite:true,sortOrder:step(i),active:true}));
/** Extra adult ₹800, extra child ₹400, extra bed ₹1,000 per night (only for types that allow an extra bed), child age 6–12. Illustrative. */
export const seedOccupancy=(rooms:Pick<Room,'type'>[]):RmsOccRow[]=>seedRoomTypes(rooms).map((t,i)=>({id:'occ-'+t.code.toLowerCase(),code:'X-'+t.code,name:`${t.name} extras`,
  roomType:t.code,sortOrder:step(i),active:true,extraAdult:dm(800),extraChild:dm(400),...(t.extraBedAllowed?{extraBed:dm(1000)}:{}),childAgeFrom:6,childAgeTo:12}));

// ───────────────────────────── M6–M8 · Meal plans, extras, discount reasons ─────────────────────────────
export const seedMealPlans=():CatMealPlan[]=>[
  {id:'mpl-ep',code:'EP',name:'Room only (EP)',sortOrder:10,active:true,meals:[],addOn:dm(0),gst:'separate',roomTypes:[]},
  {id:'mpl-cp',code:'CP',name:'Breakfast included (CP)',sortOrder:20,active:true,meals:['B'],addOn:dm(300),gst:'bundled',roomTypes:[]},
  {id:'mpl-map',code:'MAP',name:'Breakfast and dinner (MAP)',sortOrder:30,active:true,meals:['B','D'],addOn:dm(750),gst:'separate',roomTypes:[]},
  {id:'mpl-ap',code:'AP',name:'All meals (AP)',sortOrder:40,active:true,meals:['B','L','D'],addOn:dm(1200),gst:'separate',roomTypes:[]},
];
export function seedExtras():CatExtra[]{
  const x=(code:string,name:string,category:string,rupees:number,gst:CatExtra['gst'],unit:string,department:string,i:number):CatExtra=>
    ({id:'ext-'+code.toLowerCase(),code,name,sortOrder:step(i),active:true,category,price:dm(rupees),gst,unit,department});
  // "Laundry" ₹500 at the laundry rate (18 %) is the worked ₹4,265 GST journey's charge (tests/run.mjs, stream-c, stream-f, m1).
  return [
    x('LAUNDRY','Laundry','laundry',500,'laundry','bag','laundry',0),
    x('DRYCLEAN','Dry cleaning','laundry',250,'laundry','piece','laundry',1),
    x('XBED','Extra bed','room',1000,'other','night','housekeeping',2),
    x('PICKUP','Airport pickup','transport',1200,'other','trip','transport',3),
    x('DROP','Airport drop','transport',1200,'other','trip','transport',4),
    x('MINIBAR','Minibar','fnb',250,'food','each','restaurant',5),
    x('LATECO','Late checkout','misc',1000,'other','each','frontdesk',6),
    x('HALL','Meeting hall','misc',2500,'banquet','hour','banquet',7),
  ];
}
export function seedDiscountReasons():CatDiscountReason[]{
  const r=(code:string,name:string,maxPct:number,approvalAbovePct:number|undefined,roles:Role[],i:number):CatDiscountReason=>{
    const row:CatDiscountReason={id:'dsr-'+code.toLowerCase(),code,name,sortOrder:step(i),active:true,maxPct:maxPct*100,roles};
    if(approvalAbovePct!==undefined)row.approvalAbovePct=approvalAbovePct*100;return row;};
  return [
    r('SERVICE','Service recovery',25,10,[],0),
    r('CORP','Corporate rate',20,undefined,[],1),
    r('REPEAT','Repeat guest',10,undefined,[],2),
    r('LONGSTAY','Long stay',15,undefined,['Owner','Manager'],3),
    r('MGMT','Management courtesy',100,undefined,['Owner','Manager'],4),
  ];
}

// ───────────────────────────── M15–M18 · Money masters ─────────────────────────────
// = the old constants (folio FOLIO_TENDERS, POS_TENDERS, closing EXPENSE_METHODS / TENDERS / EXPENSE_CATEGORIES, accounting ACC_DEFAULT_MAP)
// + "Company credit" (receipts of authorised company credit).
export const SEED_TILL='TILL';
export const SEED_BANK='BANK-1';
export const SEED_ACCOUNTS:MnyAccount[]=[
  {id:'ba-till',code:SEED_TILL,name:'Front-desk cash drawer',sortOrder:10,active:true,kind:'till'},
  {id:'ba-bank-1',code:SEED_BANK,name:'Hotel current account',sortOrder:20,active:true,kind:'bank',bank:'Demo Bank (fictional)',accountLast4:'4321',ifsc:'DEMO0001234',defaultFor:['upi','card','bank']},
];
export const SEED_METHODS:MnyMethod[]=[
  {id:'pm-cash',code:'CASH',name:'Cash',sortOrder:10,active:true,kind:'cash',settlesTo:SEED_TILL,needsReference:false,usableAt:['frontdesk','pos','expenses'],ledger:'1000'},
  {id:'pm-upi',code:'UPI',name:'UPI',sortOrder:20,active:true,kind:'upi',settlesTo:SEED_BANK,needsReference:true,usableAt:['frontdesk','pos','expenses'],ledger:'1010'},
  {id:'pm-card',code:'CARD',name:'Card',sortOrder:30,active:true,kind:'card',settlesTo:SEED_BANK,needsReference:true,usableAt:['frontdesk','pos'],ledger:'1020'},
  {id:'pm-bank',code:'BANK',name:'Bank transfer',sortOrder:40,active:true,kind:'bank',settlesTo:SEED_BANK,needsReference:true,usableAt:['frontdesk','expenses'],ledger:'1030'},
  {id:'pm-vendor-credit',code:'VCREDIT',name:'Vendor credit',sortOrder:50,active:true,kind:'credit',needsReference:false,usableAt:['expenses'],ledger:'2200'},
  {id:'pm-company-credit',code:'COMPANY',name:'Company credit',sortOrder:60,active:true,kind:'credit',needsReference:false,usableAt:[]},
];
export const SEED_CATEGORIES:MnyCategory[]=[
  {id:'ec-supplies',code:'SUPPLIES',name:'Supplies',sortOrder:10,active:true,ledger:'5000',needsBill:false},
  {id:'ec-laundry',code:'LAUNDRY',name:'Laundry',sortOrder:20,active:true,ledger:'5010',needsBill:false},
  {id:'ec-maintenance',code:'MAINT',name:'Maintenance',sortOrder:30,active:true,ledger:'5020',needsBill:false},
  {id:'ec-utilities',code:'UTIL',name:'Utilities',sortOrder:40,active:true,ledger:'5030',needsBill:false},
  {id:'ec-petty-cash',code:'PETTY',name:'Petty cash',sortOrder:50,active:true,ledger:'5040',needsBill:false},
];
/** Code of a vendor seeded from free text: V-<WORDS> (capitals, digits, “-”; ≤ 20), unique among `taken`. */
export function seedVendorCode(name:string,taken:Set<string>):string{
  const base=('V-'+name.toUpperCase().replace(/[^A-Z0-9]+/g,'-').replace(/^-+|-+$/g,'')).slice(0,20).replace(/-+$/,'')||'V-VENDOR';
  let code=base,n=2;while(taken.has(code)){const suf='-'+n++;code=base.slice(0,20-suf.length).replace(/-+$/,'')+suf;}
  taken.add(code);return code;
}
/** Vendor type guessed from a free-text name (seed only; the Owner corrects it in the master). */
export const seedVendorType=(name:string)=>/linen|laundry|dhobi/i.test(name)?'laundry':/power|water|broadband|electricity board|gas agency|internet/i.test(name)?'utility':'supplier';
const vendorCache=new WeakMap<object,MnyVendor[]>();
/** Every distinct free-text vendor on the expenses (name order), so every historical expense resolves to a row. Cached per expenses array. */
export function seedVendors(s:Pick<State,'expenses'>):MnyVendor[]{
  const key=s.expenses as unknown as object;const hit=vendorCache.get(key);if(hit)return hit;
  const names=[...new Set(s.expenses.map(e=>String(e.vendor??'').trim()).filter(Boolean))].sort((a,b)=>a.localeCompare(b,'en'));
  const taken=new Set<string>();
  const rows=names.map((name,i)=>{const code=seedVendorCode(name,taken);
    return {id:'ev-'+code.toLowerCase(),code,name:name.slice(0,60),sortOrder:step(i),active:true,type:seedVendorType(name),tds:false} as MnyVendor;});
  vendorCache.set(key,rows);return rows;
}

// ───────────────────────────── M26–M28 · POS (outlets, tables, items are the fixture; categories are derived) ─────────────────────────────
/** Code from a fixture id or name: upper-case, other characters → "-", at most 20 ("restaurant" → RESTAURANT, "Breads & rice" → BREADS-RICE). */
export function seedPosCode(x:string):string{
  return String(x).toUpperCase().replace(/&/g,' ').replace(/[^A-Z0-9-]+/g,'-').replace(/-+/g,'-').replace(/^-|-$/g,'').slice(0,20)||'X';
}
/** One category per distinct menu category text, in order of first appearance; code = the name-derived code (the server seeds CAT-01, CAT-02 … in name order). */
export function seedMenuCategories(s:Pick<State,'posX'>):MasterRow[]{
  const names:string[]=[];for(const m of s.posX?.menu??[])if(!names.includes(m.category))names.push(m.category);
  return names.map((n,i)=>({id:'mcat-'+seedPosCode(n).toLowerCase(),code:seedPosCode(n),name:n,sortOrder:step(i),active:true}));
}

// ───────────────────────────── M9–M14 · Front desk ─────────────────────────────
/** The sources the booking forms offered (RES_SOURCES), the website, the generic OTA and agent rows. */
export const SEED_BOOKING_SOURCES:FDMSourceRow[]=[
  {id:'bsrc-walkin',code:'WALKIN',name:'Walk-in',segment:'DIRECT',sortOrder:10,active:true},
  {id:'bsrc-phone',code:'PHONE',name:'Phone',segment:'DIRECT',sortOrder:20,active:true},
  {id:'bsrc-whatsapp',code:'WHATSAPP',name:'WhatsApp',segment:'DIRECT',sortOrder:30,active:true},
  {id:'bsrc-direct',code:'DIRECT',name:'Direct',segment:'DIRECT',sortOrder:40,active:true},
  {id:'bsrc-website',code:'WEBSITE',name:'Website',segment:'DIRECT',sortOrder:50,active:true},
  {id:'bsrc-ota',code:'OTA',name:'OTA',segment:'OTA',sortOrder:60,active:true},
  {id:'bsrc-agent',code:'AGENT',name:'Agent',segment:'AGENT',commissionBps:0,sortOrder:70,active:true},
  {id:'bsrc-corporate',code:'CORPORATE',name:'Corporate',segment:'CORPORATE',sortOrder:80,active:true},
];
const ID_NAMES=['Aadhaar (demo)','Passport (demo)','Driving licence (demo)','Voter ID (demo)','PAN (demo)','Other (demo)'];
/** Fixture values (guests.ts guestIdTypes) + the foreigner-only passport-and-visa type (plan M11). */
export const SEED_ID_TYPES:FDMIdTypeRow[]=[
  ...['AADHAAR','PASSPORT','DL','VOTER','PAN','OTHER'].map((code,i)=>({id:'idt-'+code.toLowerCase(),code,name:ID_NAMES[i],accepted:true,needsPhoto:false,foreignerOnly:false,sortOrder:step(i),active:true})),
  {id:'idt-visa',code:'VISA',name:'Passport + visa (demo, foreign national)',accepted:true,needsPhoto:true,foreignerOnly:true,sortOrder:70,active:true},
];
/** The numbers the prototype already prints (DEMO/2026-27/0001, DEMO-CN/2026-27/0001) + one row per other document. */
export const SEED_NUMBER_SERIES:FDMSeriesRow[]=[
  {id:'nsr-inv',code:'INV',name:'Tax invoice',document:'INV',prefix:'DEMO',start:1,padding:4,reset:'FY',activeFrom:SEED_FROM,sortOrder:10,active:true},
  {id:'nsr-bos',code:'BOS',name:'Bill of supply',document:'BOS',prefix:'BOS',start:1,padding:4,reset:'FY',activeFrom:SEED_FROM,sortOrder:20,active:true},
  {id:'nsr-crn',code:'CRN',name:'Credit note',document:'CRN',prefix:'DEMO-CN',start:1,padding:4,reset:'FY',activeFrom:SEED_FROM,sortOrder:30,active:true},
  {id:'nsr-rcp',code:'RCP',name:'Receipt',document:'RCP',prefix:'RCP',start:1,padding:5,reset:'NEVER',activeFrom:SEED_FROM,sortOrder:40,active:true},
  {id:'nsr-kot',code:'KOT',name:'Kitchen order ticket',document:'KOT',prefix:'KOT',start:1,padding:4,reset:'NEVER',activeFrom:SEED_FROM,sortOrder:50,active:true},
  {id:'nsr-reg',code:'REG',name:'Registration card',document:'REG',prefix:'REG',start:1,padding:5,reset:'NEVER',activeFrom:SEED_FROM,sortOrder:60,active:true},
];
/** "Default" = today's global behaviour: free cancellation up to arrival, no cancellation or no-show charge, no advance required. */
export const SEED_POLICIES:FDMPolicyRow[]=[{id:'pol-default',code:'DEFAULT',name:'Default',freeCancelHours:0,cancelChargeBps:0,noShowChargeBps:0,depositBps:0,sources:[],sortOrder:10,active:true}];

// ───────────────────────────── M19–M22 · People ─────────────────────────────
/** One designation per distinct designation text of the staff master (first department / pay type seen), in order of appearance. */
export function seedDesignations(s:Pick<State,'staffX'>):HRDesignation[]{
  const out:HRDesignation[]=[],seen=new Set<string>(),codes=new Set<string>();
  for(const st of s.staffX?.staff??[]){
    const name=String(st.designation??'').trim();if(!name||seen.has(name.toLowerCase()))continue;seen.add(name.toLowerCase());
    let code=seedSlug(name)||'DESIG',n=2;while(codes.has(code)){const suf='-'+n++;code=(seedSlug(name)||'DESIG').slice(0,20-suf.length)+suf;}codes.add(code);
    const rule:HRPayRule=st.payType==='daily'?'daily':(st.structure??'S')==='B'?'monthly-basic':'monthly-structured';
    out.push({id:'dsg-'+code,code,name:name.slice(0,60),sortOrder:step(out.length),active:true,dept:st.dept,defaultPayRule:rule});
  }
  return out;
}
/** Shift times of the prototype since module 17 (07–15, 15–23, 23–07, 09–18); break 30 minutes; fixed weekly off. */
export const SEED_SHIFTS:{code:string;name:string;start:string;end:string;night?:boolean}[]=[
  {code:'M',name:'Morning',start:'07:00',end:'15:00'},{code:'E',name:'Evening',start:'15:00',end:'23:00'},
  {code:'N',name:'Night',start:'23:00',end:'07:00',night:true},{code:'G',name:'General',start:'09:00',end:'18:00'}];
export const seedShiftPatterns=():MasterRow[]=>SEED_SHIFTS.map((x,i)=>({id:'shp-'+x.code,code:x.code,name:x.name,sortOrder:step(i),active:true,start:x.start,end:x.end,breakMinutes:30,night:!!x.night,weeklyOffRule:'fixed' as const}));
export const SEED_LEAVES:{code:string;name:string;paid:boolean;tracked?:'CL'|'SL'|'EL';encashment?:boolean}[]=[
  {code:'CL',name:'Casual leave',paid:true,tracked:'CL'},{code:'SL',name:'Sick leave',paid:true,tracked:'SL'},{code:'EL',name:'Earned leave',paid:true,tracked:'EL',encashment:true},
  {code:'CO',name:'Compensatory off',paid:true},{code:'ML',name:'Maternity / paternity leave',paid:true},{code:'LOP',name:'Leave without pay',paid:false}];
/** Quotas = the staff leave policy of the state (CL 12 · SL 7 · EL 15 in the fixture). */
export function seedLeaveTypes(s:Pick<State,'staffX'>):MasterRow[]{
  const ent=s.staffX?.leavePolicy?.ent??{CL:12,SL:7,EL:15};
  return SEED_LEAVES.map((x,i)=>({id:'lvt-'+x.code,code:x.code,name:x.name,sortOrder:step(i),active:true,paid:x.paid,...(x.tracked?{yearlyQuota:ent[x.tracked]}:{}),carryForward:0,encashment:!!x.encashment}));
}
/** DEMO DATA — illustrative holiday calendar (owner / CA to confirm each year). Paid = national and major public holidays; restricted = optional.
 *  Seeded only while MASTERS_SEED_OPTIONS.demoHolidays is on; every row carries `demo:true`. DEC-083 and both servers seed none. */
export const SEED_DEMO_HOLIDAYS:{code:string;name:string;kind:HRHolidayKind}[]=[
  {code:'2026-08-15',name:'Independence Day',kind:'paid'},{code:'2026-09-14',name:'Ganesh Chaturthi',kind:'restricted'},
  {code:'2026-10-02',name:'Gandhi Jayanti',kind:'paid'},{code:'2026-10-20',name:'Dussehra',kind:'paid'},
  {code:'2026-11-08',name:'Diwali (Lakshmi Puja)',kind:'paid'},{code:'2026-12-25',name:'Christmas',kind:'paid'},
  {code:'2027-01-26',name:'Republic Day',kind:'paid'},{code:'2027-05-01',name:'Maharashtra Day',kind:'paid'}];
export const seedHolidays=():MasterRow[]=>!MASTERS_SEED_OPTIONS.demoHolidays?[]:
  SEED_DEMO_HOLIDAYS.map((x,i)=>({id:'hol-'+x.code,code:x.code,name:x.name,sortOrder:step(i),active:true,kind:x.kind,demo:true}));

// ───────────────────────────── M23–M25 · Housekeeping and guest services ─────────────────────────────
export const SEED_HK_CHECKLIST=['Linen & bathroom','Surfaces & amenities','Final room check'];
export const seedHkTaskTypes=():HktRow[]=>[
  {id:'hkt-dep',code:'DEP',name:'Departure clean',sortOrder:10,active:true,appliesTo:['checkout','move'],standardMinutes:40,needsInspection:true,checklist:SEED_HK_CHECKLIST.join('\n')},
  {id:'hkt-stay',code:'STAY',name:'Stayover service',sortOrder:20,active:true,appliesTo:['stayover'],standardMinutes:20,needsInspection:false,checklist:SEED_HK_CHECKLIST.join('\n')},
  {id:'hkt-deep',code:'DEEP',name:'Deep clean',sortOrder:30,active:true,appliesTo:['deep'],standardMinutes:90,needsInspection:true,
    checklist:['Mattress, linen and curtains','Bathroom descale and grout','Furniture, vents and fittings','Final room check'].join('\n')},
  {id:'hkt-insp',code:'INSP',name:'Inspection',sortOrder:40,active:true,appliesTo:['inspection'],standardMinutes:10,needsInspection:false,checklist:['Bathroom and linen','Surfaces and amenities','Fittings and lights'].join('\n')},
];
/** One zone per floor ("<floor name> section") with the same round-robin default attendant as before; `floorLabel` lets the caller name floors from the floors master. */
export function seedHkZones(s:Pick<State,'rooms'|'users'>,floorLabel:(n:number)=>string=n=>'Floor '+n):HkzRow[]{
  const hks=s.users.filter(u=>u.enabled&&u.role==='Housekeeper').sort((a,b)=>a.id<b.id?-1:1),floors=[...new Set(s.rooms.map(r=>r.floor))].sort((a,b)=>a-b);
  return floors.map(n=>({id:'hkz-'+n,code:'Z'+n,name:`${floorLabel(n)} section`,sortOrder:n*10,active:true,floors:['F'+n],rooms:[],...(hks.length?{defaultAttendant:hks[(n-1)%hks.length].id}:{})}));
}
/** Today's eight complaint categories (code, name, department, SLA hours — illustrative targets). */
export const SEED_COMPLAINT_CATEGORIES:[string,string,GSVDept,number][]=[['CLEAN','Room cleanliness','Housekeeping',4],['MAINT','Maintenance','Maintenance',8],['NOISE','Noise','Front desk',2],
  ['STAFF','Staff behaviour','Front desk',24],['FNB','Food & beverage','Front desk',8],['BILLING','Billing','Front desk',24],['AMEN','Amenities','Housekeeping',8],['OTHER','Other','Front desk',24]];
/** Today's seven found-item categories (valuables are held in the safe; SLA = hours to trace and inform the owner). */
export const SEED_LF_CATEGORIES:[string,string,GSVDept,number,boolean][]=[['JEWEL','Jewellery','Front desk',4,true],['ELEC','Electronics','Front desk',4,true],['CASH','Cash / wallet','Front desk',4,true],
  ['DOCS','Documents','Front desk',4,true],['CLOTH','Clothing','Housekeeping',24,false],['CARE','Personal care','Housekeeping',24,false],['OTHER','Other','Housekeeping',48,false]];
export const seedComplaintCategories=():GcatRow[]=>SEED_COMPLAINT_CATEGORIES.map(([code,name,department,slaHours],i)=>({id:'gcc-'+code.toLowerCase(),code,name,sortOrder:step(i),active:true,department,slaHours}));
export const seedLostFoundCategories=():LfcRow[]=>SEED_LF_CATEGORIES.map(([code,name,department,slaHours,valuable],i)=>({id:'lfc-'+code.toLowerCase(),code,name,sortOrder:step(i),active:true,department,slaHours,valuable}));

// ───────────────────────────── The registry: master id → seed rows ─────────────────────────────
/** Seed rows of every seeded master (ids = the registered master ids). A spec's `seed` hook is `s=>mastersSeed(id,s)`. */
export const MASTERS_SEED:Record<string,(s:State)=>MasterRow[]>={
  floors:s=>seedFloors(s.rooms),
  roomTypes:s=>seedRoomTypes(s.rooms),
  amenities:()=>seedAmenities(),
  occupancyCharges:s=>seedOccupancy(s.rooms),
  mealPlans:()=>seedMealPlans(),
  extras:()=>seedExtras(),
  discountReasons:()=>seedDiscountReasons(),
  paymentMethods:()=>SEED_METHODS.map(r=>({...r,usableAt:[...r.usableAt]})),
  bankAccounts:()=>SEED_ACCOUNTS.map(r=>({...r,...(r.defaultFor?{defaultFor:[...r.defaultFor]}:{})})),
  expenseCategories:()=>SEED_CATEGORIES.map(r=>({...r})),
  expenseVendors:s=>seedVendors(s).map(r=>({...r})),
  menuCategories:s=>seedMenuCategories(s),
  bookingSources:()=>SEED_BOOKING_SOURCES.map(r=>({...r})),
  idTypes:()=>SEED_ID_TYPES.map(r=>({...r})),
  numberSeries:()=>SEED_NUMBER_SERIES.map(r=>({...r})),
  policies:()=>SEED_POLICIES.map(r=>({...r,sources:[]})),
  designations:s=>seedDesignations(s),
  shiftPatterns:()=>seedShiftPatterns(),
  leaveTypes:s=>seedLeaveTypes(s),
  holidays:()=>seedHolidays(),
  hkTaskTypes:()=>seedHkTaskTypes(),
  hkZones:s=>seedHkZones(s),
  complaintCategories:()=>seedComplaintCategories(),
  lostFoundCategories:()=>seedLostFoundCategories(),
};
/** Seed rows of a master (fresh copies; [] for a master without a seed). */
export const mastersSeed=(id:string,s:State):MasterRow[]=>MASTERS_SEED[id]?.(s)??[];
/** Ids of every seeded master. */
export const mastersSeedIds=()=>Object.keys(MASTERS_SEED);
