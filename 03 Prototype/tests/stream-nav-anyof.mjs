// Nav items may require any-of several permissions (owner request 24 Oct 2026).
export function register({test,M,assert}){
  test('[NAV] can() accepts an any-of permission list',()=>{const s=M.seed();
    assert.equal(M.can(s,'housekeeper',['payments','housekeeping']),true);assert.equal(M.can(s,'housekeeper',['payments','admin']),false);
    assert.equal(M.can(s,'reception','payments'),M.can(s,'reception',['payments']));});
  test('[NAV] My shifts & pay reachable by every enabled role; checklists by floor and desk staff',()=>{const s=M.seed();
    for(const u of s.users.filter(u=>u.enabled))assert.ok(M.can(s,u.id,['housekeeping','reservations','payments','reports','admin']),u.role);
    for(const id of ['reception','cashier','supervisor','housekeeper','maint'])assert.ok(M.can(s,id,['housekeeping','payments']),id);});
}
