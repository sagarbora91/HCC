// F12-CAP-GUEST · guest captures in the production sync layer (MEAS-CONTRACT §2, §6).
//   server  POST /commands/MeasSurveyAdd | MeasReviewSave | MeasRootCauseListSave | MeasRootCauseSet | MeasLoyaltySet
//           tables meas.survey, meas.review_month, meas.root_cause (migration 4820); rules mirror Hcc.Operations/Meas/GuestRules.cs.
// Command-only area: s.measX is filled by the one shared loader meas-captures.ts; every mapper reloads it.
// The public QR page posts MeasSurveyAdd without a session (rate-limited on the server); a desk entry needs PERM-MEAS-002.
import type {SyncArea,CommandCtx} from './types';

const text=(v:unknown)=>String(v??'').trim();
const num=(v:unknown)=>v===undefined||v===null||v===''?undefined:Number(v);

export const measGuest:SyncArea={
  id:'measGuest',
  loaders:[],
  commands:[
    {type:'MEAS_SURVEY_ADD',reload:['measX'],async send(c:CommandCtx){const k=c.cmd as Record<string,unknown>;
      await c.api.command('MeasSurveyAdd',{clientId:text(k.id)||c.key,via:text(k.via),booking:text(k.booking)||undefined,overall:num(k.overall),cleanliness:num(k.cleanliness),
        staff:num(k.staff),value:num(k.value),breakfast:num(k.breakfast),recommend:num(k.recommend),comment:text(k.comment)||undefined,consent:k.consent===true});}},
    {type:'MEAS_REVIEW_SAVE',reload:['measX'],async send(c:CommandCtx){const k=c.cmd as Record<string,unknown>;
      await c.api.command('MeasReviewSave',{month:text(k.month),platform:text(k.platform),count:num(k.count),avgScore:num(k.avgScore),score12m:num(k.score12m),
        responded:num(k.responded),themes:text(k.themes)||undefined});}},
    {type:'MEAS_ROOTCAUSE_LIST_SAVE',reload:['measX'],async send(c:CommandCtx){const rows=Array.isArray(c.cmd.rows)?c.cmd.rows as Record<string,unknown>[]:[];
      await c.api.command('MeasRootCauseListSave',{rows:rows.map(r=>({code:text(r.code),label:text(r.label),active:r.active===true}))});}},
    {type:'MEAS_ROOTCAUSE_SET',reload:['measX'],async send(c:CommandCtx){await c.api.command('MeasRootCauseSet',{complaint:text(c.cmd.complaint),code:text(c.cmd.code)});}},
    {type:'MEAS_LOYALTY_SET',reload:['measX'],async send(c:CommandCtx){await c.api.command('MeasLoyaltySet',{customer:text(c.cmd.customer),tag:text(c.cmd.tag)});}},
  ],
  unmapped:{},
};
