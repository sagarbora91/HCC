// F12 MEAS · meas-forecast fixtures. Fictional data only. NOT wired into BASE / FY26 (the pinned seed hashes stay unchanged): tests, the
// demo pages and the back-test use these explicitly.
import type {MeasEvent,MeasCompetitor,MeasRateShop} from '../../domain/streams/measure-types';
import type {FCStay} from '../../domain/forecast';
import {addDays} from '../../domain/money';

export const measFcEventsSeed=():MeasEvent[]=>[
  {id:'EVT-1',name:'Winter wedding season',from:'2026-11-20',to:'2026-12-15',kind:'wedding-season',impactPct:15,note:'Fictional'},
  {id:'EVT-2',name:'State board exams',from:'2027-02-20',to:'2027-03-15',kind:'exam',impactPct:-10},
  {id:'EVT-3',name:'Agri trade fair',from:'2026-12-04',to:'2026-12-06',kind:'fair',impactPct:25},
];
export const measFcCompetitorsSeed=():MeasCompetitor[]=>[
  {id:'CMP-1',name:'Hotel Sample Residency',active:true},{id:'CMP-2',name:'Demo Grand Inn',active:true},
  {id:'CMP-3',name:'Fictional Park Lodge',active:true},{id:'CMP-4',name:'Example Comforts',active:true},
];
export const measFcRateShopSeed=():MeasRateShop[]=>[
  {id:'RS-1',date:'2026-10-19',stayDate:'2026-10-24',competitor:'CMP-1',roomType:'Classic',ratePaise:210000,withBreakfast:false,source:'phone',by:'manager'},
  {id:'RS-2',date:'2026-10-19',stayDate:'2026-10-24',competitor:'CMP-2',roomType:'Classic',ratePaise:240000,withBreakfast:true,source:'ota-site',by:'manager'},
  {id:'RS-3',date:'2026-10-19',stayDate:'2026-10-24',competitor:'CMP-3',roomType:'Classic',ratePaise:180000,withBreakfast:false,source:'ota-site',by:'manager'},
];

/** Legacy-shaped sample history for the back-test (contract §9 golden runs on the real `lgc` data on the hotel PC only): 36 rooms,
 *  FY 2023-24 … 2025-26, monthly occupancy modelled on the legacy study's FY 2025-26 level (≈ 31 %), weekend lift, 1–3 night stays,
 *  booked 0–30 days ahead, seeded so every run is identical. */
export function measFcLegacySample(rooms=36,from='2023-04-01',to='2026-03-31'):FCStay[]{
  let seed=20260405;const rnd=()=>{seed=(seed*1103515245+12345)&0x7fffffff;return seed/0x7fffffff;};
  const MONTH:Record<string,number>={'01':33,'02':31,'03':27,'04':30,'05':34,'06':24,'07':22,'08':26,'09':29,'10':35,'11':38,'12':39};
  const free:string[]=Array.from({length:rooms},()=>from); // per room: first free night
  const ids=Array.from({length:rooms},(_,i)=>String(101+i));
  const out:FCStay[]=[];
  for(let d=from;d<=to;d=addDays(d,1)){
    const dow=new Date(d+'T12:00:00Z').getUTCDay();
    const pct=(MONTH[d.slice(5,7)]??30)+(dow===5||dow===6?6:dow===0?-4:0);
    const target=Math.round(rooms*pct/100+(rnd()-0.5)*3);
    let occ=free.filter(f=>f>d).length;
    for(let i=0;i<rooms&&occ<target;i++){if(free[i]>d)continue;
      const n=1+Math.floor(rnd()*rnd()*3),lead=Math.floor(rnd()*rnd()*31);
      out.push({room:ids[i],from:d,to:addDays(d,n),bookedOn:addDays(d,-lead)});free[i]=addDays(d,n);occ++;}
  }
  return out;
}
