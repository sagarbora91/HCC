// Base fixture: one fictional 24-room property (HCC_Fixture_and_Scenario_Catalog.md §2).
// All names, emails and references are fictional. Keys (room IDs, HCC-1041…1050, user IDs, EXP-101/102) are stable.
import {State,Room,User,Booking,Task,permissions,SCHEMA} from '../domain/types';
import {extensions} from './ext';
export const BUSINESS_DATE='2026-10-24';
export function baseFixture():State{
  const roomTypes=['Classic','Classic','Deluxe','Deluxe','Classic','Suite','Deluxe','Classic'];
  const rooms:Room[]=Array.from({length:24},(_,i)=>({
    id:String((Math.floor(i/8)+1)*100+(i%8)+1),floor:Math.floor(i/8)+1,type:roomTypes[i%8],
    rate:roomTypes[i%8]==='Suite'?450000:roomTypes[i%8]==='Deluxe'?300000:200000,
    hk:[7,11,15].includes(i)?'Dirty':i===18?'Inspection':i===20?'Cleaning':'Ready',blocked:i===23}));
  const users:User[]=[
    {id:'owner',name:'Sagar · Owner',role:'Owner',enabled:true},
    {id:'manager',name:'Meera · Manager',role:'Manager',enabled:true},
    {id:'reception',name:'Aarav · Reception',role:'Reception',enabled:true},
    {id:'cashier',name:'Priya · Cashier',role:'Cashier',enabled:true},
    {id:'supervisor',name:'Neha · Supervisor',role:'Supervisor',enabled:true},
    {id:'housekeeper',name:'Ravi · Housekeeper',role:'Housekeeper',enabled:true},
    {id:'housekeeper2',name:'Kavya · Housekeeper',role:'Housekeeper',enabled:true},
    {id:'accountant',name:'Farhan · Accountant',role:'Accountant',enabled:true},
    {id:'maint',name:'Joseph · Maintenance',role:'Maintenance',enabled:true},
    {id:'techadmin',name:'Lata · Tech admin',role:'Tech admin',enabled:true}];
  const data=[['101','Ananya Rao','In house'],['102','Vikram Shah','In house'],['104','Riya Mehta','In house'],['201','Arjun Kapoor','In house'],['202','Nisha Desai','In house'],['206','Kabir Sen','In house'],['301','Tara Iyer','In house'],['302','Dev Malhotra','In house'],['103','Ishaan Bose','Confirmed'],['207','Aditi Nair','Confirmed']];
  const bookings:Booking[]=data.map(([room,guest,status],i)=>{
    const rate=rooms.find(r=>r.id===room)!.rate;
    return {id:`HCC-${1041+i}`,guest,email:`guest${i+1}@example.test`,people:i%2+1,room,
      arrival:status==='In house'?'2026-10-23':BUSINESS_DATE,departure:'2026-10-26',rate,
      source:['Direct','Phone','OTA · manual','Corporate'][i%4],status:status as Booking['status'],registered:status==='In house',
      lines:status==='In house'?[{id:`seed-night-${i}`,label:'Room night · 23 Oct',amount:rate,date:'2026-10-23',kind:'room',taxBps:1000}]:[],
      receipts:[{id:`seed-pay-${i}`,amount:status==='In house'?rate:100000,method:i%3===0?'Cash':'UPI',date:BUSINESS_DATE,kind:'payment',reference:'DEMO-'+(100+i),actor:'cashier'}],
      version:1,notes:'Fictional guest · demonstration record'};});
  const tasks:Task[]=rooms.filter(r=>r.hk!=='Ready').map((r,i)=>({id:'task-'+i,room:r.id,assignee:i%2?'housekeeper2':'housekeeper',
    status:r.hk==='Inspection'?'Inspection':r.hk==='Cleaning'?'In progress':'To do',checked:r.hk==='Inspection'?[true,true,true]:[false,false,false]}));
  const s:State={schema:SCHEMA,scenario:'BASE',date:BUSINESS_DATE,clock:BUSINESS_DATE+'T08:00',rooms,bookings,users,tasks,
    expenses:[
      {id:'EXP-101',vendor:'Fresh Linen Services',category:'Laundry',amount:180000,method:'Cash',date:BUSINESS_DATE,status:'Posted',actor:'manager'},
      {id:'EXP-102',vendor:'Morning Supplies',category:'Supplies',amount:85000,method:'UPI',date:BUSINESS_DATE,status:'Pending',actor:'reception'}],
    approvals:[],
    audit:[{id:'a0',at:BUSINESS_DATE+'T08:00',actor:'System',action:'Demo initialized',detail:'24 rooms · fictional operational records'}],
    grants:{Owner:[...permissions],Manager:permissions.filter(p=>p!=='admin'),
      Reception:['reservations','checkin','checkout','billing','payments','expenses'],
      Cashier:['billing','payments','expenses','reports'],Supervisor:['housekeeping','inspect'],Housekeeper:['housekeeping'],
      // M1 approximations on the coarse model; replaced by the resource.action matrix in M3 (BL-024).
      Accountant:['reports'],Maintenance:['housekeeping'],'Tech admin':['admin']},
    settings:{hotel:'HCC Demo Residency',taxBps:1000,discountLimit:25000,inspection:true,checkin:'14:00',checkout:'11:00'},
    openingCash:500000,shiftOpen:true,closedDates:['2026-10-23'],processed:[],sequence:1100};
  extensions.forEach(x=>x.extendBase(s));
  return s;
}
