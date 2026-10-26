// MA-10 · Phone screens for every master + money visibility (DEC-061). Names prefixed [MA10]. The browser smoke itself is
// tools/masters-smoke.mjs (Playwright, run by hand); these tests pin what it relies on without a browser.
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const {seed,masterSpecs,mstVisibleFields,mstSeesMoney,mstMaskMoney,mstSpecsOfGroup,mstMoneyFields,mstCanEdit,mstCanEditMoney,CFG_GROUPS,cfgGroupAccess}=M;
  const src=f=>readFileSync(f,'utf8');
  const isMoney=f=>!!f.money||f.type==='money'||f.type==='datedMoney';
  const NO_MONEY=['housekeeper','housekeeper2','maint','supervisor'];
  const withMasters=s=>({...s,grants:Object.fromEntries(Object.entries(s.grants).map(([r,g])=>[r,g.includes('masters')?g:[...g,'masters']]))});

  test('[MA10] 28 masters are registered and every one belongs to a Master configuration group the phone hub lists',()=>{
    const specs=masterSpecs();assert.equal(specs.length,28);
    const gids=new Set(CFG_GROUPS.map(g=>g.id));
    for(const p of specs){assert.ok(gids.has(p.group),`${p.id} group ${p.group}`);assert.ok(mstSpecsOfGroup(p.group).some(x=>x.id===p.id),p.id);}
  });
  test('[MA10] money visibility: Owner, Tech admin, Manager, Reception, Cashier, Accountant see money; housekeeping, maintenance and supervisor never',()=>{
    const s=seed();
    for(const u of ['owner','techadmin','manager','reception','cashier','accountant'])assert.equal(mstSeesMoney(s,u),true,u);
    for(const u of NO_MONEY.concat('maint'))assert.equal(mstSeesMoney(s,u),false,u);
  });
  test('[MA10] no money, dated-money or percent-of-money field is offered to housekeeping / maintenance / supervisor, even when `masters` is granted (DEC-061)',()=>{
    const s=withMasters(seed());let checked=0;
    for(const p of masterSpecs()){
      const moneyKeys=p.fields.filter(isMoney).map(f=>f.key);
      for(const u of [...NO_MONEY,'maint']){
        const keys=mstVisibleFields(s,u,p).map(f=>f.key);
        for(const k of moneyKeys){assert.ok(!keys.includes(k),`${u} sees ${p.id}.${k}`);checked++;}
      }
    }
    assert.ok(checked>=40,`only ${checked} money fields checked`);
    // the Owner still sees them all
    for(const p of masterSpecs())assert.deepEqual(mstVisibleFields(s,'owner',p).map(f=>f.key),p.fields.map(f=>f.key),p.id);
  });
  test('[MA10] granted `masters` to housekeeping: operational lists editable, money lists read-only with the Owner-only reason',()=>{
    const s=withMasters(seed());
    assert.equal(mstCanEdit(s,'housekeeper',M.mstSpec('floors')).ok,true);
    const m=mstCanEdit(s,'housekeeper',M.mstSpec('paymentMethods'));assert.equal(m.ok,false);assert.ok(m.reason&&m.reason.length>10);
    assert.equal(mstCanEditMoney(s,'housekeeper'),false);assert.equal(mstCanEditMoney(s,'owner'),true);
  });
  test('[MA10] mstMaskMoney hides every rupee amount for a no-money role and leaves the text alone for others',()=>{
    const s=seed(),t='Room night ≤ ₹7,500 · Beverages · ₹60. deposit ₹1,000.50 and 18 %';
    assert.equal(mstMaskMoney(s,'owner',t),t);assert.equal(mstMaskMoney(s,'manager',t),t);
    for(const u of NO_MONEY.concat('maint')){const x=mstMaskMoney(s,u,t);assert.ok(!/₹\s?\d/.test(x),x);assert.ok(x.includes('18 %')&&x.includes('Room night'),x);}
  });
  test('[MA10] hub rows: every group of every role (masters granted) is either editable or read-only with a stated reason',()=>{
    const s=withMasters(seed());
    for(const u of s.users.map(x=>x.id)){
      for(const g of CFG_GROUPS){const a=cfgGroupAccess(s,u,g.id);if(a.readOnly)assert.ok(a.reason&&a.reason.length>5,`${u} ${g.id} read only without reason`);}
    }
  });
  test('[MA10] Master configuration is reachable only with admin or masters (default: Owner and Tech admin) and is registered for the phone shell',()=>{
    const s=seed();
    const navSrc=src('src/app/nav.ts');assert.ok(/id:'settings'[^}]*p:\['admin','masters'\]/.test(navSrc));
    for(const u of ['manager','reception','cashier','supervisor','housekeeper','accountant','maint'])assert.equal(M.can(s,u,['admin','masters']),false,u);
    for(const u of ['owner','techadmin'])assert.equal(M.can(s,u,['admin','masters']),true,u);
    assert.ok(/settings:MaSettings/.test(src('src/mobile/admin/register.tsx')));
    assert.ok(/'mst-row':MasterSheet/.test(src('src/features/masters/register.tsx')));
  });
  test('[MA10] phone CSS: 48 px targets for status chips, dated-money add / remove and inputs; chips wrap (no hidden third chip at 360 px / text 1.3)',()=>{
    const c=src('src/styles/m-masters.css');
    assert.ok(/\.mst-m-list \.m-chips\{[^}]*flex-wrap:wrap/.test(c));
    assert.ok(/\.mst-m-list \.m-chips button\{[^}]*min-height:48px/.test(c));
    assert.ok(/\.mst-sheet \.mst-dated-add \.btn\{[^}]*min-height:48px/.test(c));
    assert.ok(/\.mst-sheet \.mst-fields input,\.mst-sheet \.mst-fields select\{font-size:16px;min-height:48px\}/.test(c));
    assert.ok(/\.mst-m-order \.btn\{[^}]*min-width:48px;min-height:48px/.test(c));
  });
  test('[MA10] Master configuration hub (desktop and phone) masks money for no-money roles and hides the tariff / GST blocks',()=>{
    for(const f of ['src/features/admin/HotelSettings.tsx','src/mobile/admin/Settings.tsx']){const t=src(f);
      assert.ok(t.includes('cfgMoneyMask(s,uid,'),f);assert.ok(/!mstSeesMoney\(s,uid\)/.test(t),f);assert.ok(/mstSeesMoney\(s,uid\)&&<(Tariffs|GstPanel|Tax)/.test(t),f);}
    assert.ok(src('src/features/admin/config/SettingSheet.tsx').includes('cfgMoneyMask(s,uid,def.help)'));
  });
}
