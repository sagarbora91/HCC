// F12 MEAS · rpt.stock.linen-cost — Linen, laundry and guest-supply cost per occupied room (catalogue §3.10; switch meas.hk.linen, module Inventory).
// Data: inventory items, laundry batches, losses, issues (module 16) and the captured linen discards with a reason (s.measX.discards, meas-cap-hk).
// Money: Cost per occupied room-night is money (catalogue: cost behind PERM-STK-004), so the report carries moneyFlag with gate PERM-STK-004 and
// the Supervisor / Housekeeper / Maintenance / Stock roles see quantities only (DEC-061). Red row: loss above 2 % of par in the month
// (meas.target.lossPct); cost per occupied night above the target (meas.target.linenCost, when the Owner sets one).
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {invSlice,invParRequired,invTotal,invOccupied,INV_KINDS,type INVItem} from '../../streams/inventory';
import {measTarget} from '../../streams/measure';
import {T,I,M,mkRow,rangeGuard,eachDay} from './shared';

export const LINEN_COLS=[T('item','Item'),I('par','Par'),I('onHand','On hand'),I('batches','Laundry batches'),I('lost','Lost'),I('damaged','Damaged'),T('discarded','Discarded (reason)'),I('issued','Amenities issued'),M('costPerNight','Cost per occupied room-night')];
const LOST=['Missing','Taken by guest','Expired','Other'],DAMAGED=['Damaged','Stained beyond use'];
/** Loss above `lossPct` percent of par is red (strict; par 0 never red). */
export const linenLossRed=(loss:number,par:number,lossPct:number)=>par>0&&loss*100>lossPct*par;

const BASIS='Quantities in the month (any one month with data). Par = pieces required in circulation (linen). Lost = Missing, Taken by guest, Expired, Other; Damaged = Damaged, Stained beyond use and laundry returned damaged; Discarded = captured discards with their reason. Issued = supplies used. Cost per occupied room-night = (lost + damaged + discarded + issued pieces × standard cost + laundry charge at the vendor rate) ÷ occupied room-nights. Red: loss above 2 % of par, or cost per occupied night above the target when one is set.';

export const linenCost=registerReport({
  id:'rpt.stock.linen-cost',title:'Linen, laundry and guest-supply cost per occupied room',area:'stock',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-STK-004',viewPerms:['PERM-STK-001'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'},{key:'group',label:'Item group',type:'enum',options:INV_KINDS.map(k=>k.id)}],
  columns:LINEN_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf'],phone:'no',
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,31,s.date);
    const x=invSlice(s),month=p.month as string,discards=(s.measX?.discards??[]).filter(d=>d.date.startsWith(month));
    const rn=eachDay(p.from!,p.to!).reduce((a,d)=>a+invOccupied(s,d),0);
    const lossPct=measTarget(s,'meas.target.lossPct')??2,costTarget=measTarget(s,'meas.target.linenCost');
    const inMonth=(d?:string)=>!!d&&d.startsWith(month);
    const rows:RptRow[]=x.items.filter((it:INVItem)=>!p.group||it.kind===p.group).map(it=>{
      const loss=x.losses.filter(l=>l.item===it.id&&inMonth(l.date));
      const sum=(rs:string[])=>loss.filter(l=>rs.includes(l.reason)).reduce((a,l)=>a+l.qty,0);
      const batches=x.batches.filter(b=>inMonth(b.sent)&&b.lines.some(l=>l.item===it.id));
      const retDamaged=x.batches.filter(b=>inMonth(b.returned)).reduce((a,b)=>a+b.lines.filter(l=>l.item===it.id).reduce((t,l)=>t+(l.damaged??0),0),0);
      const sent=batches.reduce((a,b)=>a+b.lines.filter(l=>l.item===it.id).reduce((t,l)=>t+l.sent,0),0);
      const lost=sum(LOST),damaged=sum(DAMAGED)+retDamaged;
      const dis=discards.filter(d=>d.item===it.name||d.item===it.id),disQty=dis.reduce((a,d)=>a+d.qty,0);
      const byReason=new Map<string,number>();for(const d of dis)byReason.set(d.reason,(byReason.get(d.reason)||0)+d.qty);
      const issued=it.kind==='linen'?null:x.moves.filter(m=>m.item===it.id&&m.kind==='use'&&inMonth(m.date)).reduce((a,m)=>a+m.qty,0);
      const par=invParRequired(s,it),cost=(lost+damaged+disQty+(issued??0))*it.cost+sent*(it.laundryRate??0);
      const per=rn>0?Math.round(cost/rn):null;
      const red=linenLossRed(lost+damaged+disQty,par,lossPct)||(costTarget!==undefined&&per!==null&&per>costTarget);
      return mkRow({item:it.name,par,onHand:invTotal(it),batches:batches.length,lost,damaged,
        discarded:disQty?`${disQty} (${[...byReason].map(([r,n])=>`${r} ${n}`).join('; ')})`:'',issued,costPerNight:per},red);
    });
    const note=discards.length||(s.measX?.discards??[]).length?undefined:` Discards: capture needed — no discard reason has been captured yet (meas-cap-hk).`;
    return {params:p,columns:LINEN_COLS,rows,...(note?{about:{dateBasis:BASIS+note}}:{})};
  },
});
