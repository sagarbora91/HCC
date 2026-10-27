// Expenses (SCR-13, WF-EXP-001) with the SCC expense features (DEC-120, 27 Oct 2026). HCC's approval rules stay: an expense counts only
// once approved; the Manager approves up to ₹5,000 (or the category's limit), above goes to the Owner (or Accountant); nobody approves their
// own — nor one they changed; the Cashier never reverses or voids (DEC-063 G5, REQ-EXP-003). Added from SCC (expense.js line numbers in
// domain/streams/expenses-scc.ts): description, optional vendor (auto-added to the vendor list), a chosen date (never future, never a
// closed day), GST on the bill with SCC's prefill, bill photo, SCC's duplicate confirm, edit with a reason + change log, void with one of
// SCC's six reasons, petty-cash float (top-ups approved by a second person) and disbursements (drawer-cash expenses for approval), monthly
// budgets (+ copy last month), recurring templates (posting raises a Draft for approval), the Udhaar credit register and the month lock
// (Owner override with a reason; the month's feed turns stale). Commands: EXPENSE, EXPENSE_SUBMIT, EXPENSE_APPROVE, EXPENSE_REVERSE,
// EXP_EDIT, EXP_VOID, EXP_BUDGET_SET, EXP_BUDGET_COPY, EXP_REC_SAVE, EXP_REC_TOGGLE, EXP_REC_DELETE, EXP_REC_POST, EXP_PETTY_FLOAT,
// EXP_PETTY_FLOAT_DECIDE, EXP_PETTY_OUT, EXP_UD_ADD, EXP_UD_PAY, EXP_UD_VOID, EXP_MONTH_LOCK.
import type {Handler,Kit} from '../commands';
import type {Expense} from '../../../domain/types';
import {money,addDays} from '../../../domain/money';
import {cbIsLocked,cbLockedMessage} from '../../../domain/streams/cashbook';
import {EXPENSE_MANAGER_LIMIT,closingUnlimitedRoles,expenseDuplicates} from '../../../domain/streams/closing';
import {mnyMethod,mnyMethodFor,mnyIsDrawer,mnyCategory,mnyVendor,mnyApprovalLimit,mnyNames,mnyVendorCode,mnyVendorTypeOf,MNY_EV} from '../../../domain/streams/money-master';
import {mstFail,mstStored,mstNextSort,type MasterRow} from '../../../domain/streams/masters';
import {rbacCanSubmitExpense,rbacCanDecideExpense} from '../../../domain/streams/rbac';
import {EXP_VOID_REASONS,EXP_MAX_PAISE,EXP_PHOTO_MAX_BYTES,EXP_DESC_MAX,EXP_UD_DUE_DAYS,expSlice,expYm,expDateCheck,expGstOverMessage,expDuplicateMessage,
  expMonthLocked,expMonthLockedMessage,expMonthSummary,expMonthLockRefusal,expBudgetAlert,expBudgetCopyPlan,expRecPosted,expRecRef,expPettyDescription,
  expUdOut,expUdPayment,expUdMobile,expDueDay,expLedgerCsv,expMonthCsv,type EXPState,type ExpRecTpl,type ExpEditLog} from '../../../domain/streams/expenses-scc';

const limited=(role:string)=>!closingUnlimitedRoles.includes(role);
/** Coarse gate of edit / void: an expense user (the record decides the rest: own draft / pending, or a decider). */
const anyExpense=(s:Kit['s'],uid:string)=>rbacCanSubmitExpense(s,uid)||rbacCanDecideExpense(s,uid);
/** DEC-063 G4: role-aware guard (the Accountant decides expense items without the broad 'approve' grant). */
const need=(k:Kit,ok:(s:Kit['s'],uid:string)=>boolean,what:string)=>{if(!ok(k.s,k.uid))k.fail('FORBIDDEN',`Access denied: ${what}.`);};
/** Writable expensesX slice on the cloned state (created for old saves). */
const slice=(k:Kit):EXPState=>{const x={...expSlice(k.s)};k.s.expensesX=x;return x;};
const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
/** ₹10 crore cap (the server's; the prototype had ₹10 lakh). */
const amountOk=(k:Kit,n:unknown,msg='Enter an amount greater than zero (maximum ₹10,00,00,000).')=>{if(!(Number.isSafeInteger(n)&&(n as number)>0&&(n as number)<=EXP_MAX_PAISE))k.fail('VALIDATION',msg);return n as number;};
const findExpense=(k:Kit):Expense=>{const e=k.s.expenses.find(x=>x.id===k.cmd.id);if(!e)k.fail('NOT_FOUND','Expense not found.');return e!;};

/** SCC month-lock gate (isMonthLocked + lockedMonthOverrideModal + markFeedStale): a write dated in a tax-locked month needs the Owner and a reason;
 *  it marks that month's feed stale. Returns the audit suffix ('' when the month is not locked). */
function monthGate(k:Kit,date:string,override:unknown):string{
  const m=expYm(date);if(!expMonthLocked(k.s,date))return '';
  if(k.user.role!=='Owner')k.fail('FORBIDDEN',expMonthLockedMessage(m));
  const r=txt(override);
  if(!r)k.fail('VALIDATION',`Month ${m} is tax-locked. Give the Owner override reason (why this late change?).`);
  if(r.length>200)k.fail('VALIDATION','Keep the override reason to 200 characters.');
  const x=slice(k),f=x.months![m];x.months={...x.months,[m]:{...f,stale:true,staleAt:k.s.clock}};
  return ` · locked-month override ${m}: ${r}`;
}
/** Date gate: SCC "That day's cash statement is locked" + HCC's day-close and cash-book locks. */
function dateGate(k:Kit,date:string,method?:string){const w=expDateCheck(k.s,date,method);if(w)k.fail(w.code,w.message);}
/** Bill photo: a JPEG / PNG data URL of at most 4 MB (SCC readPhoto: "Photo too large (max 4MB)"). */
function billPhoto(k:Kit,v:unknown):string|undefined{
  if(v===undefined||v===null||v==='')return undefined;
  const m=/^data:image\/(jpeg|png);base64,([A-Za-z0-9+/]+={0,2})$/.exec(String(v));
  if(!m)k.fail('VALIDATION','The bill photo must be a JPEG or PNG image.');
  const b64=m![2],bytes=Math.floor(b64.length*3/4)-(b64.endsWith('==')?2:b64.endsWith('=')?1:0);
  if(bytes>EXP_PHOTO_MAX_BYTES)k.fail('VALIDATION','Photo too large (max 4MB)');
  return String(v);
}
/** GST on the bill: rate in basis points 0–10000 (≤ 100 %), amount in paise ≥ 0; both kept only when > 0 (SCC normEntry). */
function gst(k:Kit,amount:number):{gstRateBps?:number;gstPaise?:number}{
  const r=k.cmd.gstRateBps,g=k.cmd.gstPaise;
  if(r!==undefined&&r!==null&&r!==''&&!(Number.isSafeInteger(r)&&r>=0&&r<=10000))k.fail('VALIDATION','Enter a GST rate between 0 and 100 %.');
  if(g!==undefined&&g!==null&&g!==''&&!(Number.isSafeInteger(g)&&g>=0&&g<=EXP_MAX_PAISE))k.fail('VALIDATION','Enter the GST on the bill in rupees (0 or more).');
  const rate=Number(r)||0,paise=Number(g)||0;
  if(paise>amount&&!k.cmd.confirmGst)k.fail('VALIDATION',expGstOverMessage(paise,amount));
  return {...(rate>0?{gstRateBps:rate}:{}),...(paise>0?{gstPaise:paise}:{})};
}
/** An active category of the Expense categories master (retired / unknown refused); `keep` lets an unchanged retired value stay. */
function category(k:Kit,v:unknown,keep?:string){
  const name=txt(v);if(!name)k.fail('VALIDATION','Choose a category.');
  const cat=mnyCategory(k.s,name);
  if(cat&&!cat.active&&cat.name!==keep)mstFail('MASTER_RETIRED',`Category ${cat.name} is retired — choose an active category.`,'category');
  if(!cat)k.fail('VALIDATION','Choose a category from the list (Master configuration › Money and tax › Expense categories).');
  return cat!;
}
/** An active funding source usable for expenses (Payment methods master). */
function method(k:Kit,v:unknown){
  const row=typeof v==='string'?mnyMethod(k.s,v):undefined;
  if(row&&!row.active&&(row.usableAt??[]).includes('expenses'))mstFail('MASTER_RETIRED',`${row.name} is retired — choose an active funding source.`);
  const pm=mnyMethodFor(k.s,v,'expenses');if(!pm)k.fail('VALIDATION','Choose a funding source.');
  return pm!;
}
/** Vendor: optional (SCC); a vendor of the list must be active (retired refused); a new name is added to the list (SCC auto-add). */
function vendor(k:Kit,v:unknown):{name:string;isNew:boolean}{
  const name=txt(v);if(!name)return {name:'',isNew:false};
  if(name.length>60)k.fail('VALIDATION','Keep the vendor name to 60 characters.');
  const ven=mnyVendor(k.s,name);
  if(ven&&!ven.active)mstFail('MASTER_RETIRED',`Vendor ${ven.name} is retired — choose an active vendor.`,'vendor');
  return ven?{name:ven.name,isNew:false}:{name,isNew:true};
}
/** SCC vendor auto-add (addEntry 533, commitRecurringPost 878). While the vendor list is still the seed it is built from the expenses
 *  themselves (MA-3), so the new name is in it already; a stored list gets a new active row and a history entry. */
function addVendor(k:Kit,name:string){
  if(!name||!mstStored(k.s,MNY_EV))return;
  const x=k.s.mastersX!,rows=(x.rows[MNY_EV]??[]) as MasterRow[];
  if(rows.some(r=>String(r.name).toLowerCase()===name.toLowerCase()))return;
  const code=mnyVendorCode(name,new Set(rows.map(r=>r.code))),id=k.id('EV');
  x.rows[MNY_EV]=[...rows,{id,code,name,sortOrder:mstNextSort(rows),active:true,type:mnyVendorTypeOf(name),tds:false} as MasterRow];
  x.history=[{id:k.id('MSTH'),master:MNY_EV,rowId:id,code,field:'name',old:'—',new:`${name} (added from an expense)`,by:k.uid,at:k.s.clock},...(x.history??[])];
}
const alertText=(k:Kit,cat:string,date:string,extra=0)=>{const a=expBudgetAlert(k.s,cat,date,extra);return a?' · '+a.msg:'';};
/** Keeps the chosen date while that day is open; otherwise the business date (HCC's earlier rule: the decision dates it). */
const liveDate=(k:Kit,e:Expense)=>expDateCheck(k.s,e.date,e.method)?k.s.date:e.date;

export const handlers:Record<string,Handler>={
  // PERM-EXP-001 expense.submit. draft:true saves a Draft (own); otherwise submits for approval.
  EXPENSE:k=>{
    const {s,cmd,open,assert,id,uid}=k;need(k,rbacCanSubmitExpense,'expenses permission is required');open();
    const amount=amountOk(k,cmd.amount),draft=!!cmd.draft;
    const description=txt(cmd.description);
    if(!draft)assert(description,'Description is required');
    assert(description.length<=EXP_DESC_MAX,`Keep the description to ${EXP_DESC_MAX} characters.`);
    const cat=category(k,cmd.category),pm=method(k,cmd.method),ven=vendor(k,cmd.vendor);
    const date=cmd.date===undefined||cmd.date===null||cmd.date===''?s.date:String(cmd.date);dateGate(k,date,pm.name);
    const g=gst(k,amount);
    if(description){const d=expenseDuplicates(s,description,amount,date);if(d.length&&!cmd.confirmDuplicate)k.fail('VALIDATION',expDuplicateMessage(date,amount,description));}
    if(!draft&&cat.needsBill)assert(txt(cmd.receipt),`${cat.name} expenses need the bill / receipt number.`);
    const photo=billPhoto(k,cmd.billPhoto);
    const over=monthGate(k,date,cmd.override);
    const e:Expense={id:id('EXP'),vendor:ven.name,category:cat.name,amount,method:pm.name,date,status:draft?'Draft':'Pending',actor:uid,createdAt:s.clock,...g};
    if(description)e.description=description;
    if(txt(cmd.notes))e.notes=txt(cmd.notes).slice(0,300);
    if(txt(cmd.receipt))e.receipt=txt(cmd.receipt).slice(0,120);
    if(photo)e.billPhoto=photo;
    s.expenses.push(e);
    if(ven.isNew)addVendor(k,ven.name);
    return (draft?'Expense draft saved · ':'Expense submitted · ')+(description||ven.name||cat.name)+(date!==s.date?` · dated ${date}`:'')+over+alertText(k,cat.name,date,amount);
  },
  EXPENSE_SUBMIT:k=>{
    const {s,cmd,open,assert,uid}=k;need(k,rbacCanSubmitExpense,'expenses permission is required');open();
    const e=findExpense(k);
    assert(e.status==='Draft','Only a draft can be submitted.','INVALID_TRANSITION');
    assert(e.actor===uid,'You can submit only your own drafts.','FORBIDDEN');
    assert(txt(e.description),'Description is required — add it with Edit before submitting.');
    {const cat=mnyCategory(s,e.category);if(cat?.needsBill)assert(txt(e.receipt),`${cat.name} expenses need the bill / receipt number.`);}
    const date=liveDate(k,e),over=monthGate(k,date,cmd.override);
    e.status='Pending';e.date=date;return e.id+' · submitted for approval'+over;
  },
  // PERM-EXP-002 expense.approve / reject (manager ≤ ₹5,000 or the category's limit) + PERM-EXP-003 post
  EXPENSE_APPROVE:k=>{
    const {s,cmd,open,assert,fail,uid,user}=k;need(k,rbacCanDecideExpense,'approve permission or the Accountant role is required');open();
    const e=s.expenses.find(e=>e.id===cmd.id);
    assert(e&&e.status==='Pending','Expense is no longer pending.','INVALID_TRANSITION');
    assert(!cbIsLocked(s,e!.date),cbLockedMessage(e!.date,'deciding this expense'),'DAY_CLOSED'); // DEC-065
    assert(e!.actor!==uid,'You cannot approve your own expense.','FORBIDDEN');
    if(cmd.approve)assert(!(e!.editors??[]).includes(uid),'You changed this expense — another approver decides it (nobody approves their own).','FORBIDDEN');
    const lim=mnyApprovalLimit(s,e!.category,EXPENSE_MANAGER_LIMIT); // MA-3: category override, else the hotel limit
    if(cmd.approve&&limited(user.role)&&e!.amount>lim)fail('FORBIDDEN',`Expenses above ${money(lim)} need Owner approval.`);
    if(cmd.approve&&mnyIsDrawer(s,e!.method))assert(s.shiftOpen,'Open cash shift required.','INVALID_TRANSITION');
    const date=liveDate(k,e!),over=cmd.approve?monthGate(k,date,cmd.override):'';
    e!.status=cmd.approve?'Posted':'Rejected';e!.date=date;e!.approver=uid;e!.decidedAt=s.clock;
    if(!cmd.approve&&cmd.reason?.trim())e!.reason=String(cmd.reason).trim();
    return e!.id+' · '+e!.status+over+(cmd.approve?alertText(k,e!.category,date):'');
  },
  // PERM-EXP-003 expense.reverse: Posted → Reversed + negative Posted record on today's business date (an approved expense changes only this way)
  EXPENSE_REVERSE:k=>{
    const {s,cmd,open,assert,id,uid}=k;need(k,rbacCanDecideExpense,'approve permission or the Accountant role is required (not the Cashier, DEC-063)');open();
    const e=findExpense(k);
    assert(e.status==='Posted'&&!e.reversalOf,'Only a posted expense can be reversed.','INVALID_TRANSITION');
    assert(!cbIsLocked(s,e.date),cbLockedMessage(e.date,'reversing this expense'),'DAY_CLOSED'); // DEC-065
    assert(cmd.reason?.trim(),'A reason is required to reverse an expense.');
    if(mnyIsDrawer(s,e.method))assert(s.shiftOpen,'Open cash shift required.','INVALID_TRANSITION');
    const over=monthGate(k,s.date,cmd.override);
    const rid=id('EXP');
    const r:Expense={id:rid,vendor:e.vendor,category:e.category,amount:-e.amount,method:e.method,date:s.date,status:'Posted',actor:uid,reversalOf:e.id,reason:cmd.reason.trim(),approver:uid,decidedAt:s.clock,createdAt:s.clock};
    if(e.description)r.description=e.description;if(e.gstPaise)r.gstPaise=-e.gstPaise;if(e.gstRateBps)r.gstRateBps=e.gstRateBps;if(e.source)r.source=e.source;
    s.expenses.push(r);
    e.status='Reversed';e.reversedBy=rid;e.reason=cmd.reason.trim();
    return `${e.id} reversed by ${rid} · ${money(e.amount)}`+over;
  },
  // SCC editEntry (544-611): amount, category, funding, GST, description, bill number, bill photo; reason required; change log. Draft / Pending only.
  EXP_EDIT:k=>{
    const {s,cmd,open,assert,fail,uid}=k;need(k,anyExpense,'expenses permission is required');open();const e=findExpense(k);
    if(e.status==='Posted'||e.status==='Reversed'||e.reversalOf)fail('INVALID_TRANSITION','Approved entry is locked — a posted expense changes only by a reversal and a new entry.');
    if(e.status==='Rejected')fail('INVALID_TRANSITION','A rejected expense cannot be changed — enter it again.');
    if(e.status==='Void')fail('INVALID_TRANSITION','This expense is void.');
    const own=e.actor===uid;
    if(own)need(k,rbacCanSubmitExpense,'expenses permission is required');
    else{if(e.status==='Draft')fail('FORBIDDEN','You can change only your own drafts.');need(k,rbacCanDecideExpense,'only the person who entered it, or a Manager, Accountant or Owner, can change a pending expense');}
    const reason=txt(cmd.reason);assert(reason,'Reason required');assert(reason.length<=200,'Keep the reason to 200 characters.');
    const amount=amountOk(k,cmd.amount,'Enter an amount greater than 0');
    const cat=category(k,cmd.category,e.category),pm=method(k,cmd.method);
    if(e.status==='Draft')e.date=liveDate(k,e); // a draft left on a closed day moves to the business date (as when it is submitted)
    dateGate(k,e.date,pm.name); // SCC: "Day locked — reopen first"
    const description=txt(cmd.description);assert(description.length<=EXP_DESC_MAX,`Keep the description to ${EXP_DESC_MAX} characters.`);
    if(e.status==='Pending')assert(description,'Description is required');
    const g=gst(k,amount),receipt=cmd.receipt===undefined?e.receipt:txt(cmd.receipt).slice(0,120)||undefined;
    if(e.status==='Pending'&&cat.needsBill)assert(txt(receipt),`${cat.name} expenses need the bill / receipt number.`);
    const photo=billPhoto(k,cmd.billPhoto);
    const over=monthGate(k,e.date,reason); // SCC: the edit reason doubles as the override reason
    const old:ExpEditLog['old']={amount:e.amount,category:e.category,method:e.method,description:e.description??''};
    const nu:ExpEditLog['new']={amount,category:cat.name,method:pm.name,description};
    old.gstPaise=e.gstPaise??0;old.gstRateBps=e.gstRateBps??0;nu.gstPaise=g.gstPaise??0;nu.gstRateBps=g.gstRateBps??0;
    if((e.receipt??'')!==(receipt??'')){old.receipt=e.receipt??'';nu.receipt=receipt??'';}
    if(photo){old.billPhoto=e.billPhoto||e.hasBillPhoto?'(present)':'(none)';nu.billPhoto='(changed)';}
    e.amount=amount;e.category=cat.name;e.method=pm.name;
    if(description)e.description=description;else delete e.description;
    delete e.gstPaise;delete e.gstRateBps;Object.assign(e,g);
    if(receipt)e.receipt=receipt;else delete e.receipt;
    if(photo)e.billPhoto=photo;
    e.editLog=[...(e.editLog??[]),{at:s.clock,by:uid,old,new:nu,reason}];
    if(!own&&!(e.editors??[]).includes(uid))e.editors=[...(e.editors??[]),uid];
    return `${e.id} · changed · ${reason}`+over+alertText(k,cat.name,e.date,e.status==='Pending'?amount:0);
  },
  // SCC voidEntry (612-646): one of six reasons + note; kept for audit, out of every total. Draft / Pending only (an approved expense is
  // reversed). Who: the Owner, Manager or Accountant (DEC-063 G5: never the Cashier); the person who entered it may void their own draft.
  EXP_VOID:k=>{
    const {s,cmd,open,assert,fail,uid}=k;need(k,anyExpense,'expenses permission is required');open();const e=findExpense(k);
    if(e.status==='Posted'||e.status==='Reversed'||e.reversalOf)fail('INVALID_TRANSITION','Approved entry is locked — reverse it instead.');
    if(e.status!=='Draft'&&e.status!=='Pending')fail('INVALID_TRANSITION','Only a draft or pending expense can be voided.');
    const ownDraft=e.status==='Draft'&&e.actor===uid;
    if(!ownDraft){if(e.status==='Draft')fail('FORBIDDEN','You can void only your own drafts.');need(k,rbacCanDecideExpense,'a Manager, Accountant or Owner voids a pending expense (not the Cashier, DEC-063)');}
    const reason=txt(cmd.reason);assert((EXP_VOID_REASONS as readonly string[]).includes(reason),`Choose a reason: ${EXP_VOID_REASONS.join(', ')}.`);
    const note=txt(cmd.note);assert(note.length<=200,'Keep the note to 200 characters.');
    dateGate(k,e.date);
    let over='';
    if(expMonthLocked(s,e.date)){assert(note||k.user.role!=='Owner','Month is tax-locked — add a specific note for the override');over=monthGate(k,e.date,note);}
    e.status='Void';e.voidReason=reason+(note?' — '+note:'');e.voidedBy=uid;e.voidedAt=s.clock;
    return `${e.id} · void · ${e.voidReason}`+over;
  },
  // SCC setBudget (1291): one cap per category for the current (business) month; empty / 0 = no budget. Manager, Accountant or Owner.
  EXP_BUDGET_SET:k=>{
    const {s,cmd,assert}=k;need(k,rbacCanDecideExpense,'budgets are set by a Manager, Accountant or Owner');
    const cat=txt(cmd.category);assert(cat&&mnyCategory(s,cat),'Choose a category from the list.');
    const amount=cmd.amount===''||cmd.amount===null||cmd.amount===undefined?0:cmd.amount;
    assert(Number.isSafeInteger(amount)&&amount>=0&&amount<=EXP_MAX_PAISE,'Enter the budget in rupees (0 = no budget).');
    const m=expYm(s.date),x=slice(k),name=mnyCategory(s,cat)!.name;
    x.budgets={...(x.budgets??{}),[m]:{...(x.budgets?.[m]??{}),[name]:amount}};
    return `${m} · ${name} = ${money(amount)}`;
  },
  // SCC copyBudgets (1327-1340): heads with a budget last month and none this month.
  EXP_BUDGET_COPY:k=>{
    const {s}=k;need(k,rbacCanDecideExpense,'budgets are set by a Manager, Accountant or Owner');
    const m=expYm(s.date),plan=expBudgetCopyPlan(s,m);if(plan.message)k.fail('VALIDATION',plan.message);
    const x=slice(k),src=x.budgets![plan.prev],bm={...(x.budgets?.[m]??{})};for(const c of plan.heads)bm[c]=Number(src[c]);
    x.budgets={...x.budgets,[m]:bm};
    return `${plan.prev} → ${m} · ${plan.heads.length} head${plan.heads.length>1?'s':''}`;
  },
  // SCC addRecTpl / editRecTpl (770-812): Manager, Accountant or Owner (SCC Accounts / Owner).
  EXP_REC_SAVE:k=>{
    const {s,cmd,assert,id,uid}=k;need(k,rbacCanDecideExpense,'recurring templates are kept by a Manager, Accountant or Owner');
    const name=txt(cmd.name);assert(name,'Template name is required');assert(name.length<=60,'Keep the name to 60 characters.');
    const amount=amountOk(k,cmd.amount,'Enter a typical amount greater than 0');
    const day=Math.floor(Number(cmd.dueDay));assert(day>=1&&day<=31,'Due day must be between 1 and 31');
    const cat=category(k,cmd.category),pm=method(k,cmd.method),ven=vendor(k,cmd.vendor);
    const x=slice(k),list=[...(x.recurring??[])];
    if(cmd.id){
      const i=list.findIndex(t=>t.id===cmd.id);if(i<0)k.fail('NOT_FOUND','Template not found.');
      list[i]={...list[i],name,amount,dueDay:expDueDay(day),category:cat.name,method:pm.name,vendor:ven.name};x.recurring=list;
      return `Template ${list[i].id} · ${name} · ${money(amount)} · day ${day}`;
    }
    if(list.some(t=>t.name.toLowerCase()===name.toLowerCase())&&!cmd.confirmDuplicate)k.fail('VALIDATION',`A template named "${name}" already exists. Add another?`);
    const t:ExpRecTpl={id:id('REC'),name,amount,dueDay:expDueDay(day),category:cat.name,method:pm.name,vendor:ven.name,active:true,createdAt:s.clock,createdBy:uid};
    x.recurring=[...list,t];
    return `Template ${t.id} · ${name} · ${money(amount)} · day ${day}`;
  },
  EXP_REC_TOGGLE:k=>{
    need(k,rbacCanDecideExpense,'recurring templates are kept by a Manager, Accountant or Owner');
    const x=slice(k),t=(x.recurring??[]).find(t=>t.id===k.cmd.id);if(!t)k.fail('NOT_FOUND','Template not found.');
    x.recurring=x.recurring!.map(r=>r.id===t!.id?{...r,active:!r.active}:r);
    return `${t!.name} → ${t!.active?'inactive':'active'}`;
  },
  // SCC delRecTpl (821-828): "Ledger entries already posted from it are kept."
  EXP_REC_DELETE:k=>{
    need(k,rbacCanDecideExpense,'recurring templates are kept by a Manager, Accountant or Owner');
    const x=slice(k),t=(x.recurring??[]).find(t=>t.id===k.cmd.id);if(!t)k.fail('NOT_FOUND','Template not found.');
    x.recurring=x.recurring!.filter(r=>r.id!==t!.id);
    return `Template ${t!.name} deleted · entries already posted from it are kept`;
  },
  // SCC postRecurring / commitRecurringPost (829-884): dated today, amount editable; HCC raises a DRAFT that its poster submits for approval.
  EXP_REC_POST:k=>{
    const {s,cmd,open,assert,id,uid}=k;need(k,rbacCanSubmitExpense,'expenses permission is required');open();
    const t=(expSlice(s).recurring??[]).find(t=>t.id===cmd.id);if(!t)k.fail('NOT_FOUND','Template not found.');
    assert(t!.active,'Template is inactive — activate it to post','INVALID_TRANSITION');
    const date=s.date,mm=expYm(date),ref=expRecRef(t!.id,mm);
    dateGate(k,date);
    const amount=amountOk(k,cmd.amount,'Enter an amount greater than 0');
    const cat=category(k,t!.category),pm=method(k,t!.method),ven=vendor(k,t!.vendor);
    const n=expRecPosted(s,mm)[ref]??0;
    if(n&&!cmd.again){const sum=s.expenses.filter(e=>e.sourceRef===ref&&!e.reversalOf&&!['Void','Rejected','Reversed'].includes(e.status)).reduce((a,e)=>a+e.amount,0);
      k.fail('VALIDATION',`Already posted for ${mm} (${n} entr${n===1?'y':'ies'} · ${money(sum)}). Confirming will post ANOTHER entry for this month.`);}
    const over=monthGate(k,date,cmd.override);
    const e:Expense={id:id('EXP'),vendor:ven.name,category:cat.name,amount,method:pm.name,date,status:'Draft',actor:uid,createdAt:s.clock,
      description:`${t!.name} — ${mm}`,source:'recurring',sourceRef:ref};
    s.expenses.push(e);if(ven.isNew)addVendor(k,ven.name);
    return `Posted ${t!.name} ${money(amount)} as draft ${e.id} — submit it for approval`+over+alertText(k,cat.name,date,amount);
  },
  // SCC setFloat (1225): a top-up of the petty-cash float. HCC: recorded by an expense user, approved by a second person (Manager ≤ ₹5,000).
  EXP_PETTY_FLOAT:k=>{
    const {s,cmd,open,assert,id,uid}=k;need(k,rbacCanSubmitExpense,'expenses permission is required');open();
    const amount=amountOk(k,cmd.amount,'Enter amount');const note=txt(cmd.note);assert(note.length<=200,'Keep the note to 200 characters.');
    const x=slice(k),e={id:id('PTY'),amount,date:s.date,by:uid,at:s.clock,status:'Pending' as const,...(note?{note}:{})};
    x.petty=[...(x.petty??[]),e];
    return `${e.id} · float top-up ${money(amount)} · awaiting approval`;
  },
  EXP_PETTY_FLOAT_DECIDE:k=>{
    const {s,cmd,open,assert,fail,uid,user}=k;need(k,rbacCanDecideExpense,'approve permission or the Accountant role is required');open();
    const x=slice(k),e=(x.petty??[]).find(p=>p.id===cmd.id);if(!e)k.fail('NOT_FOUND','Top-up not found.');
    assert(e!.status==='Pending','This top-up is no longer pending.','INVALID_TRANSITION');
    assert(e!.by!==uid,'You cannot approve your own request.','FORBIDDEN');
    if(cmd.approve&&limited(user.role)&&e!.amount>EXPENSE_MANAGER_LIMIT)fail('FORBIDDEN',`Top-ups above ${money(EXPENSE_MANAGER_LIMIT)} need Owner approval.`);
    const reason=txt(cmd.reason);
    x.petty=x.petty!.map(p=>p.id===e!.id?{...p,status:cmd.approve?'Approved' as const:'Rejected' as const,decidedBy:uid,decidedAt:s.clock,...(reason?{reason}:{})}:p);
    return `${e!.id} · float top-up ${money(e!.amount)} ${cmd.approve?'approved':'rejected'}`;
  },
  // SCC pettyOut (1226-1243): a drawer-cash expense "Petty: <for>" dated today. HCC: it goes for approval like any expense (counted once approved).
  EXP_PETTY_OUT:k=>{
    const {s,cmd,open,assert,id,uid}=k;need(k,rbacCanSubmitExpense,'expenses permission is required');open();
    const amount=amountOk(k,cmd.amount,'Enter amount');const cat=category(k,cmd.category);
    const cash=mnyNames(s,'expenses').find(n=>mnyIsDrawer(s,n));assert(cash,'No drawer-cash funding source is set up for expenses (Master configuration › Payment methods).');
    const what=txt(cmd.for);assert(what.length<=120,'Keep it to 120 characters.');
    if(cat.needsBill)assert(txt(cmd.receipt),`${cat.name} expenses need the bill / receipt number.`);
    dateGate(k,s.date);const over=monthGate(k,s.date,cmd.override);
    const eid=id('EXP');
    const e:Expense={id:eid,vendor:'',category:cat.name,amount,method:cash!,date:s.date,status:'Pending',actor:uid,createdAt:s.clock,description:expPettyDescription(what),source:'petty',sourceRef:'pty-'+eid};
    if(txt(cmd.receipt))e.receipt=txt(cmd.receipt).slice(0,120);
    s.expenses.push(e);
    return `${eid} · petty ${money(amount)} · ${e.description} · awaiting approval`+over;
  },
  // SCC addReceivable (1564-1575): never posts to cash or income.
  EXP_UD_ADD:k=>{
    const {s,cmd,open,assert,id,uid}=k;need(k,rbacCanSubmitExpense,'expenses permission is required');open();
    const name=txt(cmd.name);assert(name,'Customer name is required');assert(name.length<=80,'Keep the name to 80 characters.');
    const amount=amountOk(k,cmd.amount,'Enter a valid amount');
    const due=txt(cmd.dueDate)||addDays(s.date,EXP_UD_DUE_DAYS);assert(/^\d{4}-\d{2}-\d{2}$/.test(due),'Choose a valid due date.');
    const note=txt(cmd.note);assert(note.length<=200,'Keep the note to 200 characters.');
    const x=slice(k),r={id:id('UD'),name,mobile:expUdMobile(cmd.mobile),amount,date:s.date,dueDate:due,note,settlements:[],by:uid,at:s.clock};
    x.udhaar=[...(x.udhaar??[]),r];
    return `${r.id} · credit ${name} ${money(amount)} · due ${due}`;
  },
  // SCC payReceivable (1576-1591): more than outstanding + ₹0.50 is capped to the outstanding.
  EXP_UD_PAY:k=>{
    const {s,cmd,open,assert,id,uid}=k;need(k,rbacCanSubmitExpense,'expenses permission is required');open();
    const x=slice(k),r=(x.udhaar??[]).find(u=>u.id===cmd.id);if(!r)k.fail('NOT_FOUND','Credit not found.');
    assert(!r!.void,'This credit was written off.','INVALID_TRANSITION');
    assert(expUdOut(r!)>0,'Nothing is outstanding on this credit.','INVALID_TRANSITION');
    const typed=amountOk(k,cmd.amount,'Enter an amount');
    const pm=mnyMethodFor(s,cmd.mode,'frontdesk')??mnyMethodFor(s,cmd.mode,'expenses');assert(pm,'Choose how it was paid.');
    const p=expUdPayment(r!,typed);
    x.udhaar=x.udhaar!.map(u=>u.id===r!.id?{...u,settlements:[...u.settlements,{id:id('UDP'),amount:p.amount,date:s.date,mode:pm!.name,by:uid,at:s.clock}]}:u);
    return `${r!.id} · ${r!.name} paid ${money(p.amount)}${p.capped?' (more than outstanding — capped)':''}`;
  },
  // SCC voidReceivable (1592-1600): write off / remove, reason optional, kept for audit. HCC: a Manager, Accountant or Owner.
  EXP_UD_VOID:k=>{
    const {s,cmd,assert,uid}=k;need(k,rbacCanDecideExpense,'a Manager, Accountant or Owner writes off a credit');
    const x=slice(k),r=(x.udhaar??[]).find(u=>u.id===cmd.id);if(!r)k.fail('NOT_FOUND','Credit not found.');
    assert(!r!.void,'This credit is already written off.','INVALID_TRANSITION');
    const reason=txt(cmd.reason);assert(reason.length<=200,'Keep the reason to 200 characters.');
    x.udhaar=x.udhaar!.map(u=>u.id===r!.id?{...u,void:true,voidReason:reason,voidAt:s.date,voidBy:uid}:u);
    return `${r!.id} · ${r!.name} written off · outstanding ${money(expUdOut(r!))}${reason?' · '+reason:''}`;
  },
  // SCC genTaxFeed (1486-1516): Owner only; not a future month; regenerating replaces the feed (and clears "stale"). No unlock (SCC).
  EXP_MONTH_LOCK:k=>{
    const {s,cmd,fail,uid,user}=k;
    if(user.role!=='Owner')fail('FORBIDDEN','Only the Owner can lock the month / generate the tax feed.');
    const m=txt(cmd.month),why=expMonthLockRefusal(s,m);if(why)fail('VALIDATION',why);
    const sum=expMonthSummary(s,m),x=slice(k);
    x.months={...(x.months??{}),[m]:{month:m,...sum,generatedAt:s.clock,by:uid}};
    return `${m} tax feed generated; expense ${money(sum.expense)} · GST paid on purchases ${money(sum.gstPaid)} · month locked`;
  },
  // SCC exportLedgerCSV / exportMonthCSV: recorded in the audit; the demo screen saves the file built on the device (production: the server
  // builds it, GET /expenses/export/*). The Owner, Manager or Accountant (DEC-063 G13: the Cashier does not export reports).
  EXP_CSV:k=>{
    const {s,cmd,fail}=k;need(k,rbacCanDecideExpense,'the Owner, Manager or Accountant exports expenses (not the Cashier, DEC-063 G13)');
    if(cmd.kind==='ledger'){const f=expLedgerCsv(s);return `${f.name} · ${f.rows} record${f.rows===1?'':'s'}`;}
    if(cmd.kind==='month'&&/^\d{4}-\d{2}$/.test(String(cmd.month??''))){const f=expMonthCsv(s,String(cmd.month));return `${f.name} · ${f.rows} record${f.rows===1?'':'s'}`;}
    return fail('VALIDATION','Choose what to export.');
  },
};
