// ACC-3 · lane acc3-carry — C1 asset put to use and disposal (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7, §9; rulebook F2, F4).
//   ACC_ASSET_PUT_TO_USE {asset, date}                          Owner / Accountant (PERM-ACC-004): the date the asset was first used —
//                                                                depreciation counts from it (≥ 180 days in the year full rate, else half, F2).
//   ACC_ASSET_DISPOSE {asset, date?, proceeds, method?, reason?} Owner / Accountant (PERM-ACC-004): sale / scrap. Rulebook F4: a sale is a
//                                                                Receipt Dr tender · Cr block (the sale value) through the manual-voucher API
//                                                                (the Owner's approved at once, the Accountant's waits); nothing received =
//                                                                no voucher; a block that goes negative or empty carries the flag
//                                                                "short-term capital gain / loss — CA" (no automatic profit / loss entry).
// Production: clients/sync/accounting-assets.ts (server Hcc.Application/Accounting/AssetService.cs).
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {accSlice,accMapping} from '../../../domain/streams/accounting';
import {accManualIsCashBank} from '../../../domain/streams/accounting-manual';
import {accPost} from '../../../domain/streams/accounting-parties';
import {mnyMethodFor} from '../../../domain/streams/money-master';
import {accAssets,accAssetPutToUseRefusal,accAssetDisposalFlag,ACC_ASSET_ROLES} from '../../../domain/streams/accounting-assets';
import {accManualSave} from './accounting-manual';

const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
function role(k:Kit){k.req('reports');if(!ACC_ASSET_ROLES.includes(k.user.role))k.fail('FORBIDDEN','Access denied: only the Owner or the Accountant changes the fixed-asset register (PERM-ACC-004).');}
const find=(k:Kit)=>{const id=txt(k.cmd.asset);const a=accAssets(k.s).find(x=>x.id===id||x.no===id);if(!a)k.fail('NOT_FOUND','Asset not found.');return a!;};
const write=(k:Kit,id:string,patch:Record<string,unknown>)=>{const x={...accSlice(k.s)};x.assets=(x.assets??[]).map(a=>a.id===id?{...a,...patch}:a);k.s.accountingX=x;};

export const handlers:Record<string,Handler>={
  ACC_ASSET_PUT_TO_USE:k=>{
    role(k);const a=find(k),date=txt(k.cmd.date)||k.s.date;
    const why=accAssetPutToUseRefusal(k.s,a,date);if(why)k.fail('VALIDATION',why);
    write(k,a.id,{putToUse:date});
    return `${a.no} · ${a.name} put to use ${date}${a.putToUse?` (was ${a.putToUse})`:''} · depreciation counts from this date (rulebook F2)`;
  },
  ACC_ASSET_DISPOSE:k=>{
    const {s,cmd}=k;role(k);const a=find(k);
    k.assert(!a.disposed,`${a.no} is already disposed.`,'INVALID_TRANSITION');
    const date=txt(cmd.date)||s.date;
    k.assert(/^\d{4}-\d{2}-\d{2}$/.test(date),'Enter the disposal date (YYYY-MM-DD).');
    k.assert(date>=a.date,`The disposal cannot be before the purchase (${a.date}).`);k.assert(date<=s.date,'The disposal date cannot be after the business date.');
    const proceeds=cmd.proceeds===undefined||cmd.proceeds===null||cmd.proceeds===''?0:cmd.proceeds;
    k.assert(Number.isSafeInteger(proceeds)&&(proceeds as number)>=0&&(proceeds as number)<=10_000_000_000,'Enter the sale value in paise (0 for a scrap with nothing received).');
    const p=proceeds as number,reason=txt(cmd.reason);k.assert(reason.length<=200,'Keep the reason to 200 characters.');
    const flag=accAssetDisposalFlag(s,a,p,date);
    let voucher='',method='';
    if(p>0){
      const pm=mnyMethodFor(s,cmd.method,'expenses')??mnyMethodFor(s,cmd.method,'frontdesk');if(!pm)k.fail('VALIDATION','Choose how the sale value was received.');
      method=pm!.name;const code=accMapping(s)['method:'+method];
      k.assert(code&&accManualIsCashBank(s,code),`${method} does not settle to a cash or bank ledger.`);
      const v=accManualSave(k,{date,type:'Receipt',template:'journal',reference:a.no,
        narration:`Fixed asset sold · ${a.no} ${a.name} · block ${a.ledger} credited with the sale value (rulebook F4)${reason?' · '+reason:''}`,
        lines:[{account:accPost(s,code!,{method}),debit:p,credit:0},{account:a.ledger,debit:0,credit:p}]}).voucher;
      voucher=v.id;
    }
    write(k,a.id,{disposed:{date,amount:p,voucher,...(method?{method}:{}),...(flag?{flag}:{})}});
    return `${a.no} · ${a.name} disposed ${date} · ${p?`sold for ${money(p)} by ${method} · Dr tender · Cr ${a.ledger} · voucher ${voucher}`:'scrapped, nothing received · no voucher'}${flag?' · '+flag:''}`;
  },
};
