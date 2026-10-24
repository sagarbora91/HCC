// T-04 (Tab S7 acceptance): "1 night · 1 guest", never "1 nights · 1 guests".
export function register({test,M,assert}){
  test('[T04] plural(): singular for 1, plural otherwise, custom plural form',()=>{
    assert.equal(M.plural(1,'night'),'1 night');
    assert.equal(M.plural(2,'night'),'2 nights');
    assert.equal(M.plural(0,'guest'),'0 guests');
    assert.equal(M.plural(1,'child','children'),'1 child');
    assert.equal(M.plural(3,'child','children'),'3 children');
  });
}
