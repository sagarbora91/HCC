// Compliance calendar = SCC tax module in full (DEC-116) — command handlers. Command types MUST start with 'CMP_'. Return value = audit text.
// SCC rules: done (filed-on, notes, evidence ref, reviewer — all optional; L2402–2425), not applicable (no reason, additive; L2179–2195),
// FY switch (L234–239), firm profile (L1187–1216), archive key / category (L1465–1486), custom obligations (L1376–1462), evidence files (L2281–2316).
// HCC platform safeguards kept: Owner + Accountant only (cmpHasAccess, DEC-055); the Owner ('approve') sets the profile, archives, manages custom
// obligations and settings, and is the only one who can undo a done / NA record — the reopen keeps what it removed (SCC deletes it). Audit per command.
import type {Handler,Kit} from '../commands';
import {
  CMPState,CMPCustom,CMPRecord,CMP_MONTHS,CMP_CATS,CMP_GST_TYPES,CMP_FREQS,CMP_MAX_PACKS,CMP_MAX_REOPENED,CMP_FILE_MAX_BYTES,CMP_LIMITS,CMP_FY_MIN,CMP_FY_MAX,
  CMP_ENTITIES,cmpSlice,cmpHasAccess,cmpFindCard,cmpItemMonth,cmpRecord,cmpParseDue,cmpRenderFYText,cmpStripHtml,cmpFyLabel,cmpBuiltInObligations,
  cmpPackCounts,cmpPackLines,cmpTypeLabel,cmpCatLabel,cmpFyOfDate,cmpLicenceObligations,type CMPProfile,
} from '../../../domain/streams/compliance';
import {nights} from '../../../domain/money';

const slice=(k:Kit):CMPState=>{const x=cmpSlice(k.s);k.s.complianceX=x;return x;};
const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const access=(k:Kit)=>{if(!cmpHasAccess(k.s,k.uid))k.fail('FORBIDDEN','Access denied: the compliance calendar is for the Owner and the Accountant.');};
const manage=(k:Kit)=>{access(k);k.req('approve');};
const ISO=/^\d{4}-\d{2}-\d{2}$/;
const validDate=(d:string)=>ISO.test(d)&&new Date(d+'T00:00:00Z').toISOString().slice(0,10)===d;
const fyOf=(k:Kit,x:CMPState)=>{const v=k.cmd.fy===undefined?x.fyStart:Number(k.cmd.fy);
  k.assert(Number.isInteger(v)&&v>=CMP_FY_MIN&&v<=CMP_FY_MAX,`Choose a financial year from ${cmpFyLabel(CMP_FY_MIN)} to ${cmpFyLabel(CMP_FY_MAX)}.`);return v;};
const limit=(k:Kit,v:string,max:number,what:string)=>{k.assert(v.length<=max,`${what} is limited to ${max} characters.`);return v;};
/** The card being recorded: it must exist in the catalogue (or be a custom obligation's month) — SCC findItemMonth. DEC-133: a licence card
 *  only in the FY of its expiry date. */
const card=(k:Kit,x:CMPState,fy:number)=>{const id=txt(k.cmd.itemId);const c=cmpFindCard(x,id,fy);k.assert(c,'That compliance item is not in the calendar.','NOT_FOUND');
  return {id,card:c!,month:cmpItemMonth(x,id)!};};
/** SCC: a firm must be selected to record (L2163, L2181, L2404). HCC: the property's compliance profile (the Owner sets it). */
const firm=(k:Kit,x:CMPState)=>k.assert(x.profile,'Set the compliance firm profile first (Owner: Compliance calendar › Firm profile).','INVALID_TRANSITION');
const bucket=(x:CMPState,fy:number)=>(x.records[String(fy)]??={});
const label=(c:{name:string},fy:number)=>cmpStripHtml(cmpRenderFYText(c.name,fy));
/** DEC-133: the licence part of the profile audit text (same on the server). */
const cmpLicenceAudit=(p:CMPProfile)=>`${p.fssaiExpiry?' · FSSAI licence expires '+p.fssaiExpiry:''}${p.lodgingExpiry?' · lodging licence expires '+p.lodgingExpiry:''}`;
const lines=(v:unknown):string[]=>(Array.isArray(v)?v.map(String):String(v??'').split('\n')).map(s=>s.trim()).filter(Boolean);

export const handlers:Record<string,Handler>={
  // SCC confirmDone (L2402–2425): every field optional; filed-on any date (default today = the business date); the record replaces the earlier one.
  // HCC safeguard: a done item is not overwritten — the Owner reopens it first (keeps the old record).
  CMP_DONE:k=>{
    access(k);const x=slice(k);firm(k,x);const fy=fyOf(k,x),{id,card:c}=card(k,x,fy),cur=cmpRecord(x,fy,id);
    k.assert(cur?.na!==true,'This item is marked Not Applicable (NA) and is read-only. Remove NA first to record a filing (Owner: Reopen).','INVALID_TRANSITION');
    k.assert(!cur?.done,'Already marked filed. The Owner can reopen it.','INVALID_TRANSITION');
    const filedOn=txt(k.cmd.filedOn)||k.s.date;k.assert(validDate(filedOn),'Filed on must be a valid date.');
    const notes=limit(k,txt(k.cmd.notes),CMP_LIMITS.notes,'Notes'),evidenceRef=limit(k,txt(k.cmd.evidenceRef),CMP_LIMITS.evidenceRef,'Evidence'),
      reviewerNote=limit(k,txt(k.cmd.reviewerNote),CMP_LIMITS.reviewer,'Reviewer');
    const dueDate=cmpParseDue(c.due,fy)||cur?.dueDate||null;
    const rec:CMPRecord={done:true,filedOn,notes,evidenceRef,reviewerNote,...(dueDate?{dueDate}:{}),by:k.uid,at:k.s.clock};
    bucket(x,fy)[id]=rec;
    const late=dueDate?nights(dueDate,filedOn):0;
    return `${label(c,fy)} · ${cmpFyLabel(fy)} · filed on ${filedOn}${evidenceRef?' · evidence '+evidenceRef:''}${late>0?` · ${late} day(s) after the due date`:''}`;},

  // SCC toggleNA, marking side (L2187–2192): additive na flag, no reason; the other fields stay.
  CMP_NA:k=>{
    access(k);const x=slice(k);firm(k,x);const fy=fyOf(k,x),{id,card:c}=card(k,x,fy),cur=cmpRecord(x,fy,id);
    k.assert(cur?.na!==true,'Already marked not applicable. The Owner can reopen it.','INVALID_TRANSITION');
    bucket(x,fy)[id]={...(cur??{}),na:true,naBy:k.uid,naAt:k.s.clock};
    return `${label(c,fy)} · ${cmpFyLabel(fy)} · not applicable`;},

  // SCC un-NA (L2183–2186: removes the na flag only) and undo done (L2159–2167: deletes the record) — HCC: Owner only, with a reason, and the
  // removed record is kept in the reopened log (platform safeguard).
  CMP_REOPEN:k=>{
    manage(k);const x=slice(k);const fy=fyOf(k,x),id=txt(k.cmd.itemId),cur=cmpRecord(x,fy,id);
    k.assert(cur&&(cur.na===true||cur.done),'This item is not filed or marked not applicable.','INVALID_TRANSITION');
    const reason=txt(k.cmd.reason);k.assert(reason.length>=5&&reason.length<=120,'Give a reason of 5–120 characters.');
    const part:'done'|'na'=cur!.na===true?'na':'done',b=bucket(x,fy);
    // un-NA keeps a done record underneath (SCC); without one, nothing is left (SCC deletes the emptied record).
    if(part==='na'){const r={...cur!};delete r.na;delete r.naBy;delete r.naAt;if(r.done)b[id]=r;else delete b[id];}
    else delete b[id];
    x.reopened=[{fy,itemId:id,part,record:cur!,reopenedBy:k.uid,reopenedAt:k.s.clock,reopenReason:reason},...(x.reopened??[])].slice(0,CMP_MAX_REOPENED);
    const c=cmpFindCard(x,id);
    return `${c?label(c,fy):id} · ${cmpFyLabel(fy)} · reopened (was ${part==='na'?'not applicable':'filed'+(cur!.filedOn?' on '+cur!.filedOn:'')}${cur!.evidenceRef?' · evidence '+cur!.evidenceRef:''}) · ${reason}`;},

  // SCC switchFY (L234–239): FY 2026–27 … 2032–33. DEC-125: the pick holds until the next 1 April of the business date (fyChosenIn = the
  // business date's FY now); after that the calendar follows the business date again (cmpEffectiveFy).
  CMP_FY_SET:k=>{access(k);const x=slice(k);const fy=fyOf(k,x);x.fyStart=fy;x.fyChosenIn=cmpFyOfDate(k.s.date);return `Compliance calendar · ${cmpFyLabel(fy)}`;},

  // SCC saveFirm (L1187–1216): name required; PAN / GSTIN upper-cased, no format check; QRMP only for regular / both. One profile per property (Owner).
  CMP_PROFILE_SET:k=>{
    manage(k);const x=slice(k),c=k.cmd;
    const name=limit(k,txt(c.name),CMP_LIMITS.firmName,'Firm name');k.assert(name,'Please enter a firm name.');
    const gstType=txt(c.gstType)||'regular';k.assert((CMP_GST_TYPES as string[]).includes(gstType),'Choose the GST registration type.');
    const entity=txt(c.entity)||'proprietorship';k.assert(CMP_ENTITIES.some(e=>e.id===entity),'Choose the entity type.');
    // DEC-133: licence numbers (free text) and expiry dates, all optional; a field the command leaves out keeps its value, '' clears it.
    const lic=(field:'fssaiNo'|'lodgingNo'|'fssaiExpiry'|'lodgingExpiry',what:string,date:boolean)=>{
      if(c[field]===undefined||c[field]===null)return x.profile?.[field]??'';
      const v=txt(c[field]);if(date)k.assert(!v||validDate(v),`${what} must be a valid date.`);else limit(k,v,CMP_LIMITS.licenceNo,what);return v;};
    const fssaiNo=lic('fssaiNo','FSSAI licence number',false),fssaiExpiry=lic('fssaiExpiry','FSSAI licence expiry',true),
      lodgingNo=lic('lodgingNo','Lodging licence number',false),lodgingExpiry=lic('lodgingExpiry','Lodging licence expiry',true);
    // ACC-2 integration (one fix for the tds and gst lanes' notes): the three Owner-only money switches (contract §4; DEC-058, DEC-142, DEC-146)
    // survive a profile save — a field left out keeps its value; changing one is the Owner's alone.
    const prev=x.profile,sw:Partial<Pick<CMPProfile,'gstOnAdvances'|'hotelDeductsTds'|'tdsDeductorType'>>={};
    const keep=<K extends 'gstOnAdvances'|'hotelDeductsTds'|'tdsDeductorType'>(f:K,ok:(v:unknown)=>boolean,what:string)=>{
      const v=c[f],old=prev?.[f];
      if(v===undefined||v===null||v===old){if(old!==undefined)sw[f]=old;return;}
      k.assert(ok(v),`Choose a valid value for ${what}.`);
      if(k.user.role!=='Owner')k.fail('FORBIDDEN',`Access denied: only the Owner changes ${what} (money setting, DEC-058).`);
      sw[f]=v as CMPProfile[K];};
    keep('gstOnAdvances',v=>typeof v==='boolean','GST on advances');keep('hotelDeductsTds',v=>typeof v==='boolean','“the hotel deducts TDS”');
    keep('tdsDeductorType',v=>v==='individual'||v==='other','the TDS deductor type');
    x.profile={name,pan:limit(k,txt(c.pan).toUpperCase(),CMP_LIMITS.pan,'PAN'),gstin:limit(k,txt(c.gstin).toUpperCase(),CMP_LIMITS.gstin,'GSTIN'),
      entity:entity as never,gstType:gstType as never,hasEmployees:c.hasEmployees!==false&&c.hasEmployees!=='no',
      qrmp:(gstType==='regular'||gstType==='both')&&c.qrmp===true,notes:limit(k,txt(c.notes),CMP_LIMITS.notes,'Notes'),
      ...(fssaiNo?{fssaiNo}:{}),...(fssaiExpiry?{fssaiExpiry}:{}),...(lodgingNo?{lodgingNo}:{}),...(lodgingExpiry?{lodgingExpiry}:{}),...sw,...(sw.gstOnAdvances&&prev?.gstOnAdvances&&prev.gstOnAdvancesFrom?{gstOnAdvancesFrom:prev.gstOnAdvancesFrom}:{}),updatedBy:k.uid,updatedAt:k.s.clock} satisfies CMPProfile;
    const p=x.profile;
    return `Compliance firm profile · ${p.name} · ${cmpTypeLabel(p.gstType)}${p.qrmp?' · QRMP':''}${p.hasEmployees?'':' · no employees'}${cmpLicenceAudit(p)}`;},

  // SCC toggleArchive (L1465–1473): a built-in obligation key or a custom obligation id (cmd.obligationKey); applies everywhere.
  CMP_ARCHIVE_KEY:k=>{
    manage(k);const x=slice(k),key=txt(k.cmd.obligationKey);
    k.assert(key&&(cmpBuiltInObligations().some(o=>o.key===key)||cmpLicenceObligations().some(o=>o.key===key)||x.custom.some(c=>c.id===key)),'Unknown obligation.','NOT_FOUND');
    const i=x.archived.indexOf(key);if(i>=0)x.archived.splice(i,1);else x.archived.push(key);
    return `${x.custom.find(c=>c.id===key)?.name??key} · ${i>=0?'restored':'archived'}`;},

  // SCC toggleCatArchive (L1477–1486): a whole category (built-in + custom).
  CMP_ARCHIVE_CAT:k=>{
    manage(k);const x=slice(k),cat=txt(k.cmd.cat);k.assert((CMP_CATS as string[]).includes(cat),'Unknown category.','NOT_FOUND');
    const i=x.archivedCats.indexOf(cat as never);if(i>=0)x.archivedCats.splice(i,1);else x.archivedCats.push(cat as never);
    return `${cmpCatLabel(cat)} · category ${i>=0?'restored':'archived'}`;},

  // SCC saveCustomObligation (L1376–1416): name and ≥ 1 month required; due day 0 (last day) … 31; an edit replaces the obligation (applies to
  // every month, retroactively — owner question 13); no count limit, no duplicate-name check.
  CMP_CUSTOM_SAVE:k=>{
    manage(k);const x=slice(k),c=k.cmd;
    const name=limit(k,txt(c.name),CMP_LIMITS.name,'Name');k.assert(name,'Please enter an obligation name.');
    const months=Array.isArray(c.months)?c.months.map(String):[];k.assert(months.length,'Please select at least one applicable month.');
    k.assert(months.every(m=>(CMP_MONTHS as readonly string[]).includes(m)),'Choose months from April to March.');
    const dueDay=parseInt(String(c.dueDay),10);const day=isNaN(dueDay)?0:dueDay;k.assert(Number.isInteger(day)&&day>=0&&day<=31,'Due day must be “Last day” or 1–31.');
    const cat=txt(c.cat)||'gst';k.assert((CMP_CATS as string[]).includes(cat),'Choose a category.');
    const freq=txt(c.freq)||'Monthly';k.assert(CMP_FREQS.includes(freq),'Choose a frequency.');
    const risk=txt(c.risk);k.assert(['','High','Medium','Low'].includes(risk),'Risk must be High, Medium, Low or automatic.');
    const b=c.buffer===undefined||c.buffer===null||c.buffer===''?NaN:parseInt(String(c.buffer),10);
    const t=(v:unknown,what:string,max=CMP_LIMITS.text)=>limit(k,txt(v),max,what);
    const list=(v:unknown,what:string)=>{const l=lines(v);k.assert(l.length<=CMP_LIMITS.list,`${what}: at most ${CMP_LIMITS.list} lines.`);l.forEach(s=>limit(k,s,CMP_LIMITS.short,what));return l;};
    const editing=txt(c.id);
    if(editing)k.assert(x.custom.some(o=>o.id===editing),'Custom obligation not found.','NOT_FOUND');
    const ob:CMPCustom={id:editing||'cust-'+k.id('CMPC').split('-').pop(),cat:cat as never,name,freq,dueDay:day,months:CMP_MONTHS.filter(m=>months.includes(m)),
      form:t(c.form,'Form / portal',CMP_LIMITS.short),who:t(c.who,'Who'),what:t(c.what,'What'),why:t(c.why,'Why'),penalty:t(c.penalty,'Penalty'),
      responsible:t(c.responsible,'Responsible',CMP_LIMITS.short),reviewer:t(c.reviewer,'Reviewer',CMP_LIMITS.short),risk:risk as never,
      buffer:isNaN(b)?null:Math.min(30,Math.max(0,b)),checklist:list(c.checklist,'Checklist'),workflow:list(c.workflow,'Workflow'),evidence:list(c.evidence,'Evidence'),
      urgent:false,updatedBy:k.uid,updatedAt:k.s.clock};
    const i=x.custom.findIndex(o=>o.id===ob.id);if(i>=0)x.custom[i]=ob;else x.custom.push(ob);
    const roll=day>=29&&ob.months.some(m=>new Date(Date.UTC(2026,((CMP_MONTHS as readonly string[]).indexOf(m)+3)%12+1,0)).getUTCDate()<day);
    return `${ob.id} · ${name} ${i>=0?'updated':'added'} · ${day===0?'last day':'day '+day} of ${ob.months.map(m=>m.slice(0,3)).join(', ')}${roll?' · rolls over into the next month in the shorter months (as SCC)':''}`;},

  // SCC deleteCustomObligation (L1454–1462): removed from the calendar; its evidence files (every FY) are deleted; records stay.
  CMP_CUSTOM_DELETE:k=>{
    manage(k);const x=slice(k),id=txt(k.cmd.id),ob=x.custom.find(o=>o.id===id);k.assert(ob,'Custom obligation not found.','NOT_FOUND');
    x.custom=x.custom.filter(o=>o.id!==id);
    const gone=x.files.filter(f=>f.itemId.startsWith(id+'-'));
    if(gone.length){x.files=x.files.filter(f=>!f.itemId.startsWith(id+'-'));const store={...(k.s.complianceFilesX??{})};for(const f of gone)delete store[f.id];k.s.complianceFilesX=store;}
    return `${id} · ${ob!.name} deleted${gone.length?` · ${gone.length} evidence file(s) removed`:''}`;},

  // SCC evAttachHere / confirmDone files (L2294–2299, L2419–2424): files per FY and item (firm required). HCC limit: 5 MB a file.
  CMP_EVIDENCE_ADD:k=>{
    access(k);const x=slice(k);firm(k,x);const fy=fyOf(k,x),{id,card:c}=card(k,x,fy);
    const name=limit(k,txt(k.cmd.name)||'evidence',CMP_LIMITS.fileName,'File name'),data=String(k.cmd.dataUrl??'');
    const m=/^data:([\w.+-]+\/[\w.+-]+)?(?:;[\w.+-]+=[^;,]*)*;base64,/.exec(data);k.assert(m,'Choose a file to attach.');
    const b64=data.slice(m![0].length),pad=b64.endsWith('==')?2:b64.endsWith('=')?1:0,size=Math.floor(b64.length*3/4)-pad;
    k.assert(size<=CMP_FILE_MAX_BYTES,'An evidence file can be at most 5 MB.');
    const fid=k.id('CMPF');x.files.push({id:fid,fy,itemId:id,name,size,type:m![1]||'application/octet-stream',addedBy:k.uid,addedAt:k.s.clock});
    k.s.complianceFilesX={...(k.s.complianceFilesX??{}),[fid]:data};
    return `${label(c,fy)} · ${cmpFyLabel(fy)} · evidence attached · ${name}`;},

  // SCC evDelete (L2288–2293): removes one file.
  CMP_EVIDENCE_DELETE:k=>{
    access(k);const x=slice(k),fid=txt(k.cmd.id),f=x.files.find(e=>e.id===fid);k.assert(f,'Evidence file not found.','NOT_FOUND');
    x.files=x.files.filter(e=>e.id!==fid);const store={...(k.s.complianceFilesX??{})};delete store[fid];k.s.complianceFilesX=store;
    const c=cmpFindCard(x,f!.itemId);
    return `${c?label(c,f!.fy):f!.itemId} · ${cmpFyLabel(f!.fy)} · evidence removed · ${f!.name}`;},

  // HCC extras (kept): CA name / e-mail for the simulated pack, the "Due this week" window.
  CMP_SETTINGS_SET:k=>{
    manage(k);const x=slice(k),c=k.cmd;
    if(c.caName!==undefined){const v=txt(c.caName);k.assert(v.length>=3&&v.length<=60,'CA name must be 3–60 characters.');x.settings.caName=v;}
    if(c.caEmail!==undefined){const v=txt(c.caEmail);k.assert(/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v)&&v.length<=80,'Enter a valid e-mail address.');x.settings.caEmail=v;}
    if(c.dueSoonDays!==undefined){const v=Number(c.dueSoonDays);k.assert(Number.isInteger(v)&&v>=1&&v<=30,'“Due this week” window must be 1–30 days.');x.settings.dueSoonDays=v;}
    return `Compliance settings saved · CA ${x.settings.caName} · window ${x.settings.dueSoonDays} days`;},

  // Simulated CA pack (nothing is sent). SCC opens its shell WhatsApp composer with a "ca_pack" template that is not in the inputs.
  CMP_CA_PACK:k=>{
    access(k);const x=slice(k);
    k.assert(x.settings.caEmail,'Set the CA e-mail in Manage compliance first.');
    const c=cmpPackCounts(k.s);
    const pack={id:k.id('CMPP'),at:k.s.clock,by:k.uid,to:x.settings.caEmail,toName:x.settings.caName,counts:{overdue:c.overdue,due:c.due,filed:c.filed,na:c.na},lines:cmpPackLines(k.s)};
    x.packs=[pack,...x.packs].slice(0,CMP_MAX_PACKS);
    return `${pack.id} · CA pack to ${x.settings.caName} (simulated — nothing sent) · ${c.overdue} overdue, ${c.open} pending`;},
};
