// M0 walkthrough simulation — runs S0 against the current prototype domain build (tests/.generated/domain.mjs). Run from 03 Prototype after 'npm test': node "../05 Delivery Evidence/M0/m0_walkthrough_sim.mjs"
import {seed,execute,totals,drawer,metrics} from '../../03 Prototype/tests/.generated/domain.mjs';
let s=seed();
// S0 walkthrough adjustments
s.bookings.forEach(b=>{if(b.status==='In house')b.departure='2026-10-28';if(b.status==='Confirmed'){b.arrival='2026-10-27';b.departure='2026-10-29';}});
const log=(t)=>console.log(t, JSON.stringify({date:s.date,drawer:drawer(s)/100}));
const X=(u,c)=>{s=execute(s,u,c);};
log('start'); console.log('metrics',JSON.stringify(metrics(s)));
X('reception',{type:'BOOK',guest:'Rahul Verma (fictional)',room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:2,source:'Phone'});
const id=s.bookings.at(-1).id;
X('reception',{type:'PAYMENT',id,amount:100000,method:'UPI',kind:'payment',reference:'DEMO-UPI-0001'});
X('reception',{type:'REGISTER',id});X('reception',{type:'CHECKIN',id});
X('cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit',reference:''});
log('after checkin+deposit');
X('manager',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});
X('owner',{type:'NIGHT_POST'});
const exp24=drawer(s); X('manager',{type:'SHIFT_CLOSE',counted:exp24}); X('owner',{type:'DAY_CLOSE'}); X('owner',{type:'NEXT_DAY'}); log('day25 open');
X('reception',{type:'CHARGE',id,amount:50000,label:'Laundry service'});
X('reception',{type:'DISCOUNT',id,amount:50000,reason:'Service delay goodwill'});
const apr=s.approvals.at(-1).id;
try{X('reception',{type:'APPROVAL',id:apr,approve:true})}catch(e){console.log('reception self-approve:',e.message)}
X('manager',{type:'APPROVAL',id:apr,approve:true});
X('owner',{type:'NIGHT_POST'});
console.log('folio d25',JSON.stringify(totals(s.bookings.find(b=>b.id===id))));
const exp25=drawer(s); X('manager',{type:'SHIFT_CLOSE',counted:exp25}); X('owner',{type:'DAY_CLOSE'}); X('owner',{type:'NEXT_DAY'}); log('day26 open');
X('cashier',{type:'PAYMENT',id,amount:200000,method:'Cash',kind:'payment',reference:''});
X('cashier',{type:'PAYMENT',id,amount:140000,method:'UPI',kind:'payment',reference:'DEMO-UPI-0002'});
X('cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit-return',reference:''});
console.log('folio d26',JSON.stringify(totals(s.bookings.find(b=>b.id===id))));
X('reception',{type:'CHECKOUT',id});
const t=s.tasks.at(-1);console.log('task',JSON.stringify(t),'room',JSON.stringify(s.rooms.find(r=>r.id==='105')));
X('housekeeper',{type:'TASK',id:t.id,action:'start'});[0,1,2].forEach(i=>X('housekeeper',{type:'TASK',id:t.id,action:'check',index:i}));X('housekeeper',{type:'TASK',id:t.id,action:'complete'});X('supervisor',{type:'TASK',id:t.id,action:'inspect'});
console.log('room after',JSON.stringify(s.rooms.find(r=>r.id==='105')));
X('cashier',{type:'EXPENSE',vendor:'Aqua Water Cans',category:'Supplies',amount:30000,method:'Cash'});
X('manager',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});
X('owner',{type:'NIGHT_POST'});
const exp26=drawer(s);log('before close 26');
X('manager',{type:'SHIFT_CLOSE',counted:exp26}); X('owner',{type:'DAY_CLOSE'});
const before=JSON.stringify(metrics(s)); try{X('owner',{type:'DAY_CLOSE'})}catch(e){console.log('rerun close:',e.message)} try{X('owner',{type:'NIGHT_POST'})}catch(e){console.log('rerun post:',e.message)}
console.log('metrics26',before);
X('owner',{type:'USER',id:'reception'});try{X('reception',{type:'BOOK',guest:'X Y',room:'106',arrival:'2026-10-27',departure:'2026-10-28',people:1})}catch(e){console.log('disabled:',e.message)}
console.log('audit tail',s.audit.length);
