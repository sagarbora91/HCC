// Module 27 · Hall booking seed (F6 H1; fictional names and prices). hallSlice(s) returns this when s.hallX is absent, so BASE / FY26 carry
// no hall slice until the first hall command (SCHEMA 14). H2 adds the T&C templates (fixtures/ext/hall-seed.ts) on top of this seed.
import type {HallState} from '../../domain/streams/hall';

/** A fresh copy each call (handlers write into it). Prices before GST. */
export function hallSeed():HallState{
  return {
    venues:[
      {id:'V-SAGAR',name:'Sagar Hall',kind:'HALL',capacity:{THEATRE:200,ROUND:150,USHAPE:60,FLOATING:250,CLASSROOM:100},gstClass:'HALL_RENT',active:true,version:1,minPlates:100,
        slots:[{slot:'MORNING',from:'08:00',to:'13:00',ratePaise:1000000},{slot:'EVENING',from:'18:00',to:'23:00',ratePaise:1500000,minPlates:100},{slot:'FULL_DAY',from:'08:00',to:'23:00',ratePaise:2500000}]},
      {id:'V-MOTI',name:'Moti Hall',kind:'HALL',capacity:{THEATRE:80,ROUND:60,USHAPE:30,FLOATING:100,CLASSROOM:40},gstClass:'HALL_RENT',active:true,version:1,
        slots:[{slot:'MORNING',from:'08:00',to:'13:00',ratePaise:500000},{slot:'AFTERNOON',from:'13:00',to:'17:00',ratePaise:500000},{slot:'EVENING',from:'18:00',to:'23:00',ratePaise:800000},{slot:'FULL_DAY',from:'08:00',to:'23:00',ratePaise:1300000}]},
      {id:'V-LAWN',name:'Garden Lawn',kind:'LAWN',capacity:{ROUND:300,FLOATING:500},gstClass:'HALL_RENT',active:true,version:1,minPlates:150,
        slots:[{slot:'EVENING',from:'18:00',to:'23:00',ratePaise:2500000},{slot:'FULL_DAY',from:'08:00',to:'23:00',ratePaise:4000000}]},
      {id:'V-SAGAR-LAWN',name:'Sagar Hall + Garden Lawn',kind:'HALL',capacity:{},gstClass:'HALL_RENT',active:true,version:1,combinedOf:['V-SAGAR','V-LAWN'],
        slots:[{slot:'EVENING',from:'18:00',to:'23:00',ratePaise:3500000},{slot:'FULL_DAY',from:'08:00',to:'23:00',ratePaise:5500000}]},
    ],
    enquiries:[],quotations:[],templates:[],events:[],
    menus:[
      {id:'MP-SILVER-VEG',name:'Silver Veg',perPlatePaise:65000,veg:true,active:true,posItemIds:['m-soup','m-tikka','m-dal','m-paneer','m-roti','m-rice','m-gulab','m-kulfi']},
      {id:'MP-GOLD-VEG',name:'Gold Veg',perPlatePaise:85000,veg:true,active:true,posItemIds:['m-soup','m-tikka','m-dal','m-paneer','m-thali','m-naan','m-rice','m-gulab','m-kulfi','m-lassi']},
      {id:'MP-GOLD-NV',name:'Gold Non-veg',perPlatePaise:95000,veg:false,active:true,posItemIds:['m-soup','m-tikka','m-kebab','m-dal','m-curry','m-naan','m-rice','m-gulab','m-kulfi']},
    ],
    lostReasons:[
      {id:'LR-PRICE',label:'Price too high',needsNote:false},{id:'LR-DATE',label:'Date not available',needsNote:false},
      {id:'LR-OTHERVENUE',label:'Chose another venue',needsNote:false},{id:'LR-CANCELLED',label:'Event cancelled',needsNote:false},
      {id:'LR-NORESPONSE',label:'No response',needsNote:false},{id:'LR-OTHER',label:'Other',needsNote:true},
    ],
    settings:{validityDays:15,expiryReminderDays:2,advanceDefault:[2500,5000,'BALANCE'],secondDueDays:30,
      discountLimitBps:{RECEPTION:0,MANAGER:1000,OWNER:null},refundManagerLimitPaise:5_000_000,
      cancelSlabs:[{minDays:61,keptBps:0},{minDays:30,keptBps:2500},{minDays:7,keptBps:5000},{minDays:0,keptBps:10000}],
      paxReduceFreeBps:1000,enquiryAutoLostDays:30,enquiryWarnDays:3,softHoldMaxDays:3},
    seq:0,
  };
}
