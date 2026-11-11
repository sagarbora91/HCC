// F7-R8 · admin and audit reports on the engine: audit log (avFilter), export and print log (s.rptX.exportLog), report dictionary (reportDictionary),
// configuration and masters history (s.configHistory). Adapters; the audit log keeps its masking (mstMaskMoney, DEC-061) and detail rule.
import type {RptColumn,RptRow} from '../types';
import {registerReport,reportDef} from '../catalogue';
import {avFilter,avEntity,avEntityId} from '../../streams/audit-viewer';
import {adminAuditDetailVisible} from '../../streams/admin';
import {mstMaskMoney} from '../../streams/masters';
import {reportDictionary} from '../../streams/closing';
import {paramsText} from '../params';
import {HccError} from '../../errors';
import {col,rowOf,rangeParams,userName} from '../money/common';

const A4='PERM-ADM-004';

export const AUDIT_COLS:RptColumn[]=[col('at','Time','text'),col('actor','Actor','text'),col('action','Action','text'),col('detail','Detail','text'),col('id','Id','text')];
export const auditLog=registerReport({
  id:'rpt.admin.audit-log',title:'Audit log',area:'admin',priority:'P1',status:'built',moneyFlag:false,viewPerms:[A4],dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'actor',label:'Actor',type:'text'},{key:'action',label:'Action',type:'text'},{key:'text',label:'Contains',type:'text'}],columns:AUDIT_COLS,
  dateBasis:'Audit events by the date they were recorded, newest first. Detail is shown only to roles with audit detail; money in a detail is masked for roles that never see hotel money (DEC-061).',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,uid,p)=>{
    const show=adminAuditDetailVisible(s,uid);
    const f=avFilter(s,{from:p.from,to:p.to,actor:p.actor?String(p.actor):undefined,action:p.action?String(p.action):undefined,text:p.text?String(p.text):undefined});
    return {params:p,columns:AUDIT_COLS,rows:[...f].sort((a,b)=>b.at.localeCompare(a.at)||b.id.localeCompare(a.id)).map(a=>rowOf('row',{at:a.at.replace('T',' '),actor:a.actor,action:a.action,
      detail:show?mstMaskMoney(s,uid,a.detail):null,id:avEntityId(a)||a.id}))};
  },
});
void avEntity;

export const EXPORT_LOG_COLS:RptColumn[]=[col('at','Time','text'),col('user','User','text'),col('report','Report','text'),col('params','Parameters','text'),col('format','Format','text'),col('rows','Rows','int')];
export const exportLog=registerReport({serverMirror:false,
  id:'rpt.admin.export-log',title:'Export and print log',area:'admin',priority:'P2',status:'built',moneyFlag:false,viewPerms:[A4],dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'report',label:'Report',type:'text'},{key:'user',label:'User',type:'user'}],columns:EXPORT_LOG_COLS,
  dateBasis:'Every print and export is recorded when it is made (rule V.4): who, which report, which parameters, which format and how many rows. Newest first. Owner and Manager only.',
  formats:['screen','xlsx','pdf'],phone:'no',
  select:(s,uid,p)=>{
    const role=s.users.find(u=>u.id===uid)?.role;
    if(role!=='Owner'&&role!=='Manager')throw new HccError('FORBIDDEN','The export and print log is for the Owner and Manager.');
    const q=String(p.report??'').trim().toLowerCase();
    const log=(s.rptX?.exportLog??[]).filter(e=>e.at.slice(0,10)>=p.from!&&e.at.slice(0,10)<=p.to!&&(!p.user||e.by===p.user)
      &&(!q||e.reportId.toLowerCase().includes(q)||(reportDef(e.reportId)?.title??'').toLowerCase().includes(q)));
    return {params:p,columns:EXPORT_LOG_COLS,rows:[...log].sort((a,b)=>b.at.localeCompare(a.at)||b.id.localeCompare(a.id)).map(e=>rowOf('row',{at:e.at.replace('T',' '),user:userName(s,e.by),
      report:reportDef(e.reportId)?.title??e.reportId,params:Object.entries(paramsText(e.params as never)).map(([k,v])=>`${k}=${v}`).join(', ')||null,format:e.format,rows:e.rows}))};
  },
});

export const DICT_COLS:RptColumn[]=[col('metric','Metric','text'),col('definition','Definition','text'),col('zero','Zero handling','text')];
export const reportDictionaryReport=registerReport({serverMirror:false,
  id:'rpt.admin.report-dictionary',title:'Report dictionary',area:'admin',priority:'P2',status:'built',moneyFlag:false,viewPerms:['PERM-RPT-001','PERM-RPT-002'],params:[],columns:DICT_COLS,
  dateBasis:'Static: the figures of the closing reports as the Financial Rulebook (section 6, REQ-RPT-006) defines them.',formats:['screen','pdf','csv'],phone:'yes',
  select:(_s,_u,p)=>({params:p,columns:DICT_COLS,rows:reportDictionary.map(([a,b,c])=>rowOf('row',{metric:a,definition:b,zero:c}))}),
});

export const CONFIG_COLS:RptColumn[]=[col('at','Changed','text'),col('actor','By','text'),col('key','Setting','text'),col('before','Before','text'),col('after','After','text'),col('effective','Effective from','date'),col('reason','Reason','text')];
export const configHistory=registerReport({serverMirror:false,
  id:'rpt.admin.config-history',title:'Configuration and masters history',area:'admin',priority:'P2',status:'built',moneyFlag:false,viewPerms:['PERM-CFG-001'],dateClass:'D-B',params:rangeParams('D-B'),columns:CONFIG_COLS,
  dateBasis:'Changes of the configuration settings by the date they were made, newest first. Money values are masked for roles that never see hotel money (DEC-061).',formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,uid,p)=>({params:p,columns:CONFIG_COLS,rows:(s.configHistory??[]).filter(h=>h.at.slice(0,10)>=p.from!&&h.at.slice(0,10)<=p.to!).sort((a,b)=>b.at.localeCompare(a.at)||b.id.localeCompare(a.id))
    .map(h=>rowOf('row',{at:h.at.replace('T',' '),actor:userName(s,h.actor)||h.actor,key:h.key,before:mstMaskMoney(s,uid,h.before),after:mstMaskMoney(s,uid,h.after),effective:h.effectiveFrom||null,reason:h.reason||null}))}),
});
