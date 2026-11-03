// F7-R7 · rate card (tariff card) document: the published version of the tariff card, prices incl. GST as the sheet shows them (trcRateSheet - the same
// selector as the screen). Public-facing prices, no hotel money: the tariff-card view right (Owner, Manager, Accountant, Reception, Cashier).
import type {State} from '../../types';
import {HccError} from '../../errors';
import {trcRateSheet,trcCan} from '../../streams/tariffcard';
import {gstPct} from '../../gst';
import {type DocModel,T,R} from './model';

export function rateCardDocument(s:State,uid:string,versionId?:string):DocModel{
  if(!trcCan(s,uid,'view'))throw new HccError('FORBIDDEN','You cannot print the rate card.');
  const sh=trcRateSheet(s,versionId);if(!sh.version)throw new HccError('NOT_FOUND','No published tariff card yet.');
  const groups=[...new Set(sh.rows.map(r=>r.group))];
  return {id:'doc.rate-card',title:'Tariff card',number:sh.version.label,property:sh.hotel,money:false,
    pairs:[['Card',sh.version.id],['Valid from',sh.validFrom],['GST',sh.gstLine],['Weekend nights',sh.weekend]],
    tables:groups.map(g=>({key:g,title:g,columns:[T('item','Item'),T('nights','Nights'),T('excl','Before GST','money'),T('gst','GST'),T('incl','Price incl. GST','money')],
      rows:sh.rows.filter(r=>r.group===g).map(r=>R({item:r.title+(r.note?` (${r.note})`:''),nights:r.daySet,excl:r.excl,gst:gstPct(r.bps),incl:r.incl}))})),
    notes:['Prices per room per night unless stated; meal plans per guest per night.',...(sh.terms?[sh.terms]:[])],sign:[],basis:'Valid from the card date.'};
}
