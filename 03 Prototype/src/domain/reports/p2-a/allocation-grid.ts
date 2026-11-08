// F8-RPT-P2-A · rpt.rooms.allocation-grid — Room allocation grid (catalogue A31 / A56). Rooms against dates showing who is in which room. No money. Max 31 days.
// The definition holds 31 date slots (d01..d31); a run returns as many as the range has. Cell = guest name, OOO (out of order) or empty.
import type {RptColumn} from '../types';
import {registerReport} from '../catalogue';
import {addDays} from '../../money';
import {blockedDuring} from '../../selectors';
import {rmsOffline} from '../../streams/roomoffline';
import {guestNameOf} from '../guests/shared';
import {T,I,row,rangeGuard,roomOnNight,natural} from './common';

const slot=(i:number)=>'d'+String(i+1).padStart(2,'0');
const SLOTS:RptColumn[]=Array.from({length:31},(_,i)=>T(slot(i),'Day '+(i+1)));
const MON=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
export const dateLabel=(d:string)=>`${+d.slice(8)} ${MON[+d.slice(5,7)-1]}`;
export const ALLOCATION_COLS:RptColumn[]=[T('room','Room'),T('roomType','Room type'),...SLOTS,I('nights','Nights occupied')];

export const allocationGrid=registerReport({
  id:'rpt.rooms.allocation-grid',title:'Room allocation grid',area:'rooms',priority:'P2',status:'built',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],dateClass:'D-S',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-S'},{key:'to',label:'To',type:'date',dateClass:'D-S'},{key:'roomType',label:'Room type',type:'text'}],
  columns:ALLOCATION_COLS,
  dateBasis:'One column per night (range up to 31 days). A cell shows the guest in the room that night (confirmed, in house, departed, or a hold still running), OOO for a blocked or offline room, empty when free. Nights occupied counts the guest cells.',
  formats:['screen','xlsx','pdf'],phone:'no',legacyIds:['A31','A56'],
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,31,s.date);
    const q=String(p.roomType??'').trim().toLowerCase();
    const dates:string[]=[];for(let d=p.from!;d<=p.to!;d=addDays(d,1))dates.push(d);
    const cols:RptColumn[]=[ALLOCATION_COLS[0],ALLOCATION_COLS[1],...dates.map((d,i)=>({...SLOTS[i],label:dateLabel(d)})),ALLOCATION_COLS[ALLOCATION_COLS.length-1]];
    const holding=(b:{status:string;holdUntil?:string})=>b.status==='Confirmed'||b.status==='In house'||b.status==='Departed'||(b.status==='Held'&&(!b.holdUntil||s.clock<b.holdUntil));
    const rooms=s.rooms.filter(r=>r.status!=='retired'&&(!q||r.type.toLowerCase()===q)).sort((a,b)=>a.type.localeCompare(b.type)||natural(a.id,b.id));
    const rows=rooms.map(r=>{
      const cells:Record<string,string|number|null>={room:r.id,roomType:r.type};let n=0;
      dates.forEach((d,i)=>{
        const b=s.bookings.find(x=>holding(x)&&x.arrival<=d&&d<x.departure&&roomOnNight(x,d)===r.id);
        if(b){cells[slot(i)]=guestNameOf(s,b);n++;}
        else cells[slot(i)]=rmsOffline(r,d,addDays(d,1))||blockedDuring(r,d,addDays(d,1))?'OOO':'';
      });
      cells.nights=n;return row('row',cells,r.type);
    });
    return {params:p,columns:cols,rows};
  },
});
