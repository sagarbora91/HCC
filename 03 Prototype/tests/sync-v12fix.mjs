// APK v12 defect fixes, job A: APK12-2 sign-in throttle text, APK12-3 cash needs the user's own shift + no doubled "Not permitted:",
// APK12-1 shift-closed wording, APK12-6 automatic recovery from read-only, APK12-5 booking entry points disabled while read-only.
import {readFileSync} from 'node:fs';
import {loadSync,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const wait=ms=>new Promise(r=>setTimeout(r,ms));
export async function register({test,assert,M}){
  const S=await loadSync();

  // ── APK12-2 ──
  const signInRoutes=(res)=>({'POST /auth/sign-in':()=>res(),'GET /me/capabilities':()=>reply(401,{code:'UNAUTHENTICATED',message:'Sign in.'})});
  const thr=await startRuntime(S,fakeServer(signInRoutes(()=>reply(429,{code:'FORBIDDEN',message:'Too many sign-in attempts from this device. Wait a few minutes and try again.',details:{reason:'TOO_MANY_ATTEMPTS'}},{'Retry-After':'840'}))));
  const rThr=await thr.signIn({loginName:'anita',pin:'1234'});
  const noHdr=await startRuntime(S,fakeServer(signInRoutes(()=>reply(429,{code:'FORBIDDEN',message:'Too many sign-in attempts from this device. Wait a few minutes and try again.',details:{reason:'TOO_MANY_ATTEMPTS'}}))));
  const rNoHdr=await noHdr.signIn({loginName:'anita',pin:'1234'});
  const bad=await startRuntime(S,fakeServer(signInRoutes(()=>reply(403,{code:'FORBIDDEN',message:'Sign-in failed. Check the name and the password or PIN.'}))));
  const rBad=await bad.signIn({loginName:'anita',pin:'0000'});
  test('[v12 APK12-2] 429 TOO_MANY_ATTEMPTS shows the throttle message with the wait in minutes; BAD_CREDENTIALS keeps the generic text, with no title prefix',()=>{
    assert.equal(rThr.ok,false);assert.equal(rThr.text,'Too many sign-in attempts from this device. Wait 14 minutes, then try again.');
    assert.equal(thr.get().signInMessage,rThr.text);
    assert.match(rNoHdr.text,/Too many sign-in attempts.*Wait a few minutes/);
    assert.equal(rBad.text,'Sign-in failed. Check the name and the password or PIN.');
  });

  // ── APK12-3: doubled prefix + cash needs own shift ──
  test('[v12 APK12-3] "Not permitted:" is not repeated when the server message already carries it',()=>{
    const e=new (S.HccClientError)('FORBIDDEN','Not permitted: Outside your assigned scope');
    assert.equal(S.errorText(e),'Not permitted: Outside your assigned scope');
    assert.equal(S.errorText(new (S.HccClientError)('FORBIDDEN','Outside your assigned scope')),'Not permitted: Outside your assigned scope');
  });
  const grant=(id,scope)=>({permissionId:id,mode:'A',scope,limit:null,limitUnit:null});
  const meOf=(grants)=>S.meFromCapabilities({...capabilities({}),roles:['REC'],permissions:grants});
  const reception=meOf([grant('PERM-PAY-001','S')]),cashier=meOf([grant('PERM-PAY-001','S'),grant('PERM-SHF-001','P')]),owner=meOf([grant('PERM-PAY-001','P')]);
  test('[v12 APK12-3] cashBlockedReason: own-shift scope without the cashier permission is blocked; cashier with an open shift and property scope are not',()=>{
    assert.equal(S.cashBlockedReason(reception,true),S.CASH_NEEDS_OWN_SHIFT);assert.equal(S.cashBlockedReason(reception,false),S.CASH_NEEDS_OWN_SHIFT);
    assert.match(S.CASH_NEEDS_OWN_SHIFT,/Cash needs your own open cashier shift — use UPI\/card or ask the cashier/);
    assert.equal(S.cashBlockedReason(cashier,true),undefined);assert.equal(S.cashBlockedReason(cashier,false),S.CASH_NEEDS_OWN_SHIFT);
    assert.equal(S.cashBlockedReason(owner,true),undefined);assert.equal(S.cashBlockedReason(null,false),undefined);
  });
  const recCaps={...capabilities({}),userId:5,displayName:'Priya',roles:['REC'],permissions:[grant('PERM-PAY-001','S'),grant('PERM-FOL-001','A'),grant('PERM-RES-001','A')]};
  const rsrv=fakeServer({'GET /me/capabilities':()=>recCaps});
  const rrt=await startRuntime(S,rsrv);
  const cash=await rrt.run({type:'PAYMENT',id:'1201',amount:1000,kind:'payment',method:'Cash',key:'v12:1'});
  const cashSplit=await rrt.run({type:'PAYMENT',id:'1201',amount:1000,kind:'payment',tenders:[{method:'UPI',amount:600,reference:'a'},{method:'Cash',amount:400,reference:''}],key:'v12:2'});
  const upi=await rrt.run({type:'PAYMENT',id:'1201',amount:1000,kind:'payment',method:'UPI',reference:'U1',key:'v12:3'});
  test('[v12 APK12-3] Reception: a cash tender (also inside a split) is refused before any request; UPI is not refused by the precheck',()=>{
    assert.equal(cash.reason,'precheck');assert.equal(cash.text,'Check the details: '+S.CASH_NEEDS_OWN_SHIFT);
    assert.equal(cashSplit.reason,'precheck');assert.notEqual(upi.reason,'precheck');
    assert.equal(rsrv.commands('RecordReceipt').filter(c=>c.body.tender==='Cash').length,0);
  });

  // ── APK12-1 ──
  const open=M.seed(),closed={...M.seed(),closedDates:[M.seed().date]};
  test('[v12 APK12-1] Shift-closed wording: "Open a cashier shift first" when the day is open, the next-day text only when the day is closed',()=>{
    assert.equal(S.shiftClosedText(open),'The cash shift is closed. Open a cashier shift first.');
    assert.equal(S.shiftClosedText(closed),'The cash shift is closed. Open the next business day first.');
    const own=meOf([grant('PERM-PAY-001','P')]);assert.equal(S.cashBlockedReason(own,false),undefined);
  });
  const ownerRt=await startRuntime(S,fakeServer());
  const cashOwner=await ownerRt.run({type:'PAYMENT',id:'1201',amount:1000,kind:'payment',method:'Cash',key:'v12:4'});
  test('[v12 APK12-1] The PAYMENT precheck for an owner with no open shift says "Open a cashier shift first" on an open day',()=>{
    assert.equal(cashOwner.reason,'precheck');assert.match(cashOwner.text,/Open a cashier shift first/);assert.doesNotMatch(cashOwner.text,/next business day/);
  });

  // ── APK12-6: automatic recovery ──
  const rsv=fakeServer();
  const rec=await startRuntime(S,rsv,{recoveryMs:30,focusRefreshMs:60_000});
  const win={ev:{},addEventListener(n,f){this.ev[n]=f;},removeEventListener(n){delete this.ev[n];}};
  const detach=rec.attachWindow(win);
  rsv.down=true;await rec.refresh();const wasRo=rec.get().readOnly;
  rsv.down=false;await wait(150);
  const back=rec.get().readOnly;
  // focus / online also re-check immediately while unavailable
  rsv.down=true;await rec.refresh();const ro2=rec.get().readOnly;
  rsv.down=false;win.ev.online();await wait(60);const back2=rec.get().readOnly;
  rsv.down=true;await rec.refresh();rsv.down=false;win.ev.focus();await wait(60);const back3=rec.get().readOnly;
  detach();
  test('[v12 APK12-6] Read-only clears by itself: background timer, online event and window focus re-check health; no Retry press needed',()=>{
    assert.equal(wasRo,true);assert.equal(back,false,'timer');assert.equal(ro2,true);assert.equal(back2,false,'online');assert.equal(back3,false,'focus');
    assert.equal(Object.keys(win.ev).length,0,'listeners removed');
  });

  const src=f=>readFileSync(new URL('../src/'+f,import.meta.url),'utf8');
  test('[v12 APK12-5] New booking is blocked while read-only: open() refuses type new, act() buttons and the phone Fab are disabled with the read-only hint',()=>{
    const app=src('app/App.tsx'),fab=src('app/mobile/components.tsx');
    assert.match(app,/if\(offline&&x\.type==='new'\)\{toast\(READ_ONLY_HINT,true\);return;\}/);
    assert.match(app,/disabled=\{busy\|\|offline\} title=\{offline\?READ_ONLY_HINT:undefined\}/);
    assert.match(fab,/className="m-fab" disabled=\{offline\}/);
    assert.match(src('clients/react.tsx'),/useReadOnly=\(\)=>useConnection\(\)\.status==='unavailable'/);
  });
}
