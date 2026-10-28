// ACC-2 · lane acc2-tds — production sync (server: Hcc.Api/Endpoints/AccountingTdsEndpoints.cs; contract §4, §5, §9).
//
//   commands  ACC_TDS_SETTINGS {hotelDeductsTds?, tdsDeductorType?} → SetTdsSettings {hotelDeductsTds, deductorType} (Owner, DEC-058);
//             reloads complianceX (the profile screens show the switches).
//             ACC_TDS_REMIT {month, section, amount, method, challan, date?} → RecordTdsRemittance {month, section, amountPaise, tender, challan,
//             date} (PERM-ACC-013 Owner / Accountant); reloads accountingX.
//   loader    accountingTdsLoader (patch on complianceX ← GET /accounting/tds, PERM-ACC-013): the two switches onto the profile. NOT in this
//             area's `loaders` yet — the integrator registers it with its READS line and tests/_role-reads.json (as ACC-1 did for the opening
//             loader; ACC2-TDS-DONE.md "Integration needed"). The dated rate overrides travel through Master configuration (SETTING_SET on
//             tds.* → SetTdsRate, an admin.ts edit listed in the DONE file).
import type {CMPState} from '../../domain/streams/compliance';
import {tdsBaseSection} from '../../domain/tds';
import type {SyncArea,LoaderCtx,SliceLoader} from './types';

/** GET /accounting/tds (Hcc.Contracts.Accounting.TdsStateView). */
export type SrvTdsState={hotelDeductsTds:boolean;deductorType:'individual'|'other';
  rates:{key:string;effectiveFrom:string;value:number;reason:string}[];
  remittances:{remitId:number;month:string;section:string;amountPaise:number;tender:string;challan:string;businessDate:string}[]};

/** SetTdsSettings body: only the switches the command names (null = unchanged on the server). */
export function tdsSettingsBody(cmd:Record<string,unknown>):{hotelDeductsTds:boolean|null;deductorType:string|null}{
  return {hotelDeductsTds:typeof cmd.hotelDeductsTds==='boolean'?cmd.hotelDeductsTds:null,
    deductorType:cmd.tdsDeductorType==='individual'||cmd.tdsDeductorType==='other'?cmd.tdsDeductorType:null};
}
/** RecordTdsRemittance body (amount in paise as the prototype keeps it; the server re-checks every guard). */
export function tdsRemitBody(cmd:Record<string,unknown>){
  return {month:String(cmd.month??''),section:String(cmd.section??''),amountPaise:Number(cmd.amount??0),tender:String(cmd.method??'Bank transfer'),
    challan:String(cmd.challan??'').trim(),date:typeof cmd.date==='string'&&cmd.date?cmd.date:null};
}

async function loadTds(c:LoaderCtx):Promise<CMPState|undefined>{
  const cur=c.current('complianceX') as CMPState|undefined;
  const r=await c.api.get<SrvTdsState>('/accounting/tds');
  if(!cur?.profile)return cur;
  return {...cur,profile:{...cur.profile,hotelDeductsTds:r.hotelDeductsTds,tdsDeductorType:r.deductorType}};
}
/** Patch loader for complianceX (registered at the ACC-2 integration with READS 'GET /accounting/tds'). */
export const accountingTdsLoader:SliceLoader={slice:'complianceX',kind:'patch',requires:'reports',load:loadTds};

export const accountingTds:SyncArea={
  id:'accounting-tds',
  loaders:[],
  commands:[
    {type:'ACC_TDS_SETTINGS',reload:['complianceX'],
      precheck:cmd=>typeof cmd.hotelDeductsTds!=='boolean'&&cmd.tdsDeductorType!=='individual'&&cmd.tdsDeductorType!=='other'?'Choose “hotel deducts TDS” or the rate column.':undefined,
      async send(c){await c.api.command('SetTdsSettings',tdsSettingsBody(c.cmd));}},
    {type:'ACC_TDS_REMIT',reload:['accountingX'],
      precheck:cmd=>!/^\d{4}-(0[1-9]|1[0-2])$/.test(String(cmd.month??''))?'Choose the month the TDS was deducted in (YYYY-MM).'
        :!tdsBaseSection(String(cmd.section??''))||String(cmd.section).includes(':')?'Choose the TDS section (rent, contractor, professional or commission).'
        :!(Number.isInteger(cmd.amount)&&Number(cmd.amount)>0)?'Enter the amount remitted.'
        :String(cmd.challan??'').trim().length<3?'Enter the challan number (3–40 characters).':undefined,
      async send(c){await c.api.command('RecordTdsRemittance',tdsRemitBody(c.cmd));}},
  ],
};
