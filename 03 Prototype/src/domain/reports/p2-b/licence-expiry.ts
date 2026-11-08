// F8-RPT-P2-B · rpt.tax.licence-expiry: licences with expiry dates and days left. Source = the firm profile (FSSAI and lodging house licence number and expiry,
// DEC-133); "Renewal card" = the compliance calendar has the renewal card (it exists while the expiry date is set). The profile holds no issue date, so Issued is blank.
// Server twin: Hcc.Reporting/Reports/P2B/LicenceExpiryReport.cs. No money column.
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {HccError} from '../../errors';
import {cmpSlice,cmpLicenceCard,cmpLicenceFields,CMP_LICENCE_DEFS} from '../../streams/compliance';
import './grants';
import {col,rowOf} from '../money/common';

export const LE_COLS=[col('licence','Licence','text'),col('number','Number','text'),col('issued','Issued','date'),col('expires','Expires','date'),col('daysLeft','Days left','int'),col('card','Renewal card','bool')];
export const daysBetween=(a:string,b:string)=>Math.round((Date.parse(b)-Date.parse(a))/86400000);
export function licenceRows(s:State,within:number){
  const x=cmpSlice(s);
  return CMP_LICENCE_DEFS.flatMap(d=>{
    const f=cmpLicenceFields(d.kind),no=x.profile?.[f.no]??'',exp=x.profile?.[f.expiry]??'';
    if(!no&&!exp)return [];
    const left=exp?daysBetween(s.date,exp):null;
    if(left!==null&&left>within)return [];
    return [{key:exp||'9999-12-31',row:rowOf('row',{licence:d.name.replace(/ – Renewal$/,''),number:no,issued:null,expires:exp||null,daysLeft:left,card:cmpLicenceCard(x,d.kind)?1:0})}];
  }).sort((a,b)=>a.key<b.key?-1:a.key>b.key?1:0).map(v=>v.row);
}
export const licenceExpiry=registerReport({
  id:'rpt.tax.licence-expiry',title:'Licence expiry',area:'tax',priority:'P2',status:'built',moneyFlag:false,viewPerms:['PERM-CMP-001'],dateClass:'D-NOW',
  params:[{key:'within',label:'Within days',type:'text',default:'90'}],columns:LE_COLS,
  dateBasis:'The state now, against the business date: licences and permits of the firm profile (FSSAI, lodging house) that expire within the chosen days, expired ones included (negative days). The profile keeps no issue date.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_u,p)=>{
    const w=Number(p.within??90);if(!Number.isInteger(w)||w<0||w>3650)throw new HccError('VALIDATION','Within days: a whole number from 0 to 3650.');
    return {params:p,columns:LE_COLS,rows:licenceRows(s,w)};
  },
});
