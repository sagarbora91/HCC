import {build} from 'esbuild';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
await build({entryPoints:['src/features/admin/PolicyLimitEditor.tsx'],bundle:true,platform:'node',format:'esm',external:['react'],outfile:'tests/.generated/policy-limit-editor.mjs',logLevel:'error'});
const {PolicyLimitEditor,changedPolicyLimit}=await import('./.generated/policy-limit-editor.mjs');

export function register({test,M,assert}){
  test('[BL-01] posted discount stays Approved after expiry; pending discount still expires',()=>{
    let s=M.seed();
    s=M.execute(s,'reception',{type:'BOOK',guest:'Approval regression',room:'105',arrival:s.date,departure:'2026-10-26',people:1});
    const id=s.bookings.at(-1).id;
    for(const type of ['REGISTER','CHECKIN','POST_NIGHTS'])s=M.execute(s,'owner',{type,id});
    s=M.execute(s,'reception',{type:'DISCOUNT',id,amount:50000,reason:'Regression discount'});
    const aid=s.approvals.at(-1).id;
    s=M.execute(s,'manager',{type:'APPROVAL',id:aid,approve:true});
    const a=s.approvals.find(x=>x.id===aid),booking=s.bookings.find(x=>x.id===id);
    const total=M.totals(booking);
    s.clock=M.addMinutes(a.expiresAt,1);
    assert.equal(M.folioApprovalStatus(s,a),'Approved');
    assert.equal(M.folioApprovalExpired(s,a),false);
    assert.deepEqual(M.totals(booking),total);
    assert.throws(()=>M.execute(s,'manager',{type:'APPROVAL',id:aid,approve:true}),/no longer pending/);
    assert.equal(M.folioApprovalStatus(s,{...a,status:'Pending'}),'Expired');
    assert.equal(M.folioApprovalStatus(s,{...a,kind:undefined}),'Approved');
  });
  test('[BL-01] unused refund and company credit approvals still expire; executed decisions do not',()=>{
    const s=M.seed();
    const a={id:'expiry-test',booking:'HCC-1041',amount:100,reason:'Test',actor:'reception',version:1,status:'Approved',expiresAt:M.addMinutes(s.clock,-1)};
    for(const kind of ['refund','company-credit']){
      assert.equal(M.folioApprovalStatus(s,{...a,kind}),'Expired');
      assert.equal(M.folioApprovalStatus(s,{...a,kind,status:'Executed'}),'Executed');
    }
  });
  test('[BL-02] unchanged inherited limit has a disabled, explicitly labelled save button',()=>{
    const s=M.seed(),current=M.adminLimitFor(s,'Reception','PERM-FOL-003');
    assert.equal(M.adminPolicy(s).limits.Reception?.['PERM-FOL-003'],undefined);
    const html=renderToStaticMarkup(React.createElement(PolicyLimitEditor,{current,unit:'money',disabled:false,onSave:()=>assert.fail('render must not save')}));
    assert.match(html,/<button[^>]*disabled=""[^>]*>Save limit<\/button>/);
    assert.equal(changedPolicyLimit(String(current/100),'money',current),undefined);
    assert.equal(changedPolicyLimit('','money',current),undefined);
    assert.equal(changedPolicyLimit('abc','money',current),undefined);
    assert.equal(changedPolicyLimit('1.001','money',current),undefined);
    assert.equal(changedPolicyLimit('101','percent',10),undefined);
    assert.equal(changedPolicyLimit('0','money',current),0);
  });
  test('[BL-02] changing reservation permission leaves inherited limits and their audit history alone',()=>{
    const s=M.seed(),before=M.adminPolicy(s);
    const next=M.execute(s,'owner',{type:'POLICY_SET',role:'Reception',perm:'PERM-RES-001',effect:'none'});
    assert.equal(M.adminPolicy(next).version,before.version+1);
    assert.deepEqual(M.adminPolicy(next).limits,before.limits);
    assert.equal(next.audit.length,s.audit.length+1);
    assert.doesNotMatch(next.audit[0].detail,/Limit ·/);
    const limit=changedPolicyLimit('300','money',M.adminLimitFor(next,'Reception','PERM-FOL-003'));
    assert.equal(limit,30000);
    const saved=M.execute(next,'owner',{type:'POLICY_SET',role:'Reception',perm:'PERM-FOL-003',limit});
    assert.equal(M.adminLimitFor(saved,'Reception','PERM-FOL-003'),30000);
    assert.equal(M.adminPolicy(saved).version,before.version+2);
    assert.equal(changedPolicyLimit('300','money',30000),undefined);
  });
}
