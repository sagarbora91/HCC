// CU f8e-open: (1) the company-credit statement starts at the company's opening outstanding (the server twin reads the approved opening entry's
// 1110/CMP-id line, see CuF8eOpenCompanyOpeningTests.cs: the same figures to the paise); (3) sales by item filtered by cashier (the user who
// settled the check); (4) dark / high-contrast check of the split-folio CSS. Integer paise, fictional data.
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const T='[CU-F8E-OPEN] ';
  const CR='rpt.money.company-credit',SI='rpt.rest.sales-by-item';
  const run=(s,u,id,p={})=>M.runReport(s,u,id,p,{all:true});
  const body=r=>r.rows.filter(x=>x.kind==='row'),tot=r=>r.rows.find(x=>x.kind==='total');

  test(T+'company-credit statement: opening outstanding 40000 paise, payment 30000 incl. TDS 2000, closing 8000',()=>{
    const s=structuredClone(M.seed());
    s.groupsX={companies:[{id:'CO-CU1',name:'Opening Traders (fictional)',gstin:'',contact:'',phone:'',email:'',city:'Latur',rates:{},creditLimit:5000000,openingOutstanding:40000,limitHistory:[]}],
      blocks:[],requests:[],overrides:[],payments:[{id:'PAY-CU1',company:'CO-CU1',date:s.date,amount:30000,tds:2000,method:'NEFT',reference:'UTR-CU1'}]};
    const st=run(s,'owner',CR,{company:'CO-CU1'}).sections[1].rows;
    assert.equal(st.length,3);
    assert.equal(st[0].cells.narration,'Opening outstanding');assert.equal(st[0].cells.balance,40000);
    assert.equal(st[1].cells.payment,32000);assert.equal(st[1].cells.balance,8000);   // 40000 - (30000 + 2000 TDS)
    assert.deepEqual([tot({rows:st}).cells.credit,tot({rows:st}).cells.payment,tot({rows:st}).cells.balance],[0,32000,8000]);
  });

  const line=(id,item,name,price,qty)=>({id,item,name,price,qty,kot:'K1'});
  const order=(id,outlet,by,date,lines)=>({id,outlet,table:'1',covers:2,date,openedAt:date+'T12:00',openedBy:by,status:'Settled',version:1,lines,kots:[],
    settlement:{mode:'outlet',billNo:'B-'+id,method:'Cash',net:0,taxBps:500,tax:0,total:0,date,at:date+'T13:00',by}});
  test(T+'sales by item: cashier filter keeps only the checks that user settled; empty filter = all; unknown user = nothing',()=>{
    const s=structuredClone(M.seed());s.date='2026-10-05';
    const P=s.posX??{outlets:[],menu:[],orders:[],receipts:[],kotSeq:{},billSeq:0};
    const outs=P.outlets.length?P.outlets:[{id:'OUT-1',name:'Cafe',kind:'table'}];
    s.posX={...P,outlets:outs,menu:[],orders:[
      order('O1',outs[0].id,'cashier','2026-10-01',[line('l1','m-tea','Tea',5000,4)]),
      order('O2',outs[0].id,'manager','2026-10-01',[line('l1','m-tea','Tea',5000,6),line('l2','m-cake','Cake',15000,2)])]};
    const p={from:'2026-10-01',to:'2026-10-02'};
    assert.equal(tot(run(s,'owner',SI,p)).cells.value,20000+30000+30000);
    const c=run(s,'owner',SI,{...p,cashier:'cashier'});
    assert.equal(tot(c).cells.value,20000);assert.equal(tot(c).cells.qty,4);assert.equal(body(c).length,1);
    const m=run(s,'owner',SI,{...p,cashier:'manager'});
    assert.equal(tot(m).cells.value,60000);assert.equal(body(m).length,2);
    assert.equal(run(s,'owner',SI,{...p,cashier:'nobody'}).rows.length,0);
    // a Cashier asking for another cashier still sees only own bills: the two filters intersect
    assert.equal(run(s,'cashier',SI,{...p,cashier:'manager'}).rows.length,0);
    assert.equal(tot(run(s,'cashier',SI,{...p,cashier:'cashier'})).cells.value,20000);
  });

  // ── (4) dark and high-contrast check of the F8C / F8D / F8E folio rules ──
  test(T+'folio.css: no hard-coded colour; every text / background pair of the split-folio rules reaches AA (4.5) in dark, AAA (7) in high contrast; dup-tick border 3:1',()=>{
    const folio=readFileSync('src/styles/folio.css','utf8').replace(/\/\*[\s\S]*?\*\//g,'');
    assert.ok(!/#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/.test(folio),'folio.css must use tokens only');
    const v22=readFileSync('src/styles/v22-fixes.css','utf8').split('\n').filter(l=>/folio-dup/.test(l)).join('\n');
    assert.ok(!/#[0-9a-fA-F]{3,8}\b|\brgba?\(/.test(v22),'folio-dup rules must use tokens only');
    const css=readFileSync('src/styles/themes.css','utf8').split('\n');
    const at=open=>{const i=css.findIndex(l=>l.startsWith(open));assert.ok(i>=0,open);let j=i;while(css[j]!=='}')j++;return [i,j];};
    const grab=([a,b])=>{const m={};for(const l of css.slice(a+1,b)){const x=/^(--[\w-]+):(.+?);?$/.exec(l.trim());if(x)m[x[1]]=x[2].replace(/;$/,'');}return m;};
    const light={...grab(at(':root,[data-theme="light"]{')),...grab(at(':root{'))};
    const themes={light,dark:{...light,...grab(at('[data-theme="dark"]{'))},hc:{...light,...grab(at('[data-theme="hc"]{'))}};
    const lum=h=>{const [r,g,b]=[1,3,5].map(i=>parseInt(h.slice(i,i+2),16)/255).map(v=>v<=0.03928?v/12.92:((v+0.055)/1.055)**2.4);return .2126*r+.7152*g+.0722*b;};
    const cr=(a,b)=>{const x=lum(a),y=lum(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);};
    const res=(t,n)=>{let v=themes[t][n];for(let i=0;i<8&&v&&v.startsWith('var(');i++)v=themes[t][/var\((--[\w-]+)/.exec(v)[1]];return v;};
    const mix=(f,b,a)=>'#'+[1,3,5].map(i=>Math.round(parseInt(f.slice(i,i+2),16)*a+parseInt(b.slice(i,i+2),16)*(1-a)).toString(16).padStart(2,'0')).join('');
    const pairs=[['.folio-nights','--muted','--sand-2'],['.folio-nights.short','--c-84673b','--c-faf3e6'],['.folio-company','--muted','--surface'],['.folio-mismatch','--c-965432','--surface'],
      ['.field-error','--c-ad4239','--surface'],['.folio-unverified','--c-866a39','--c-fbf5ea'],['.folio-blockers','--c-9a5a3a','--c-fbf3ee'],['.sf-switcher .active','--primary','--primary-tint'],
      ['.sf-switcher button','--ink','--surface'],['.folio-dup-confirm','--warn-text','--warn-bg'],['.folio-dup-tick','--ink','--surface'],['.sf-row small','--muted','--surface']];
    for(const t of ['dark','hc']){
      const need=t==='hc'?7:4.5;
      for(const [n,f,b] of pairs){const fg=res(t,f),bg=res(t,b);assert.ok(/^#/.test(fg)&&/^#/.test(bg),t+' '+n+' resolves');assert.ok(cr(fg,bg)>=need,t+' '+n+' '+cr(fg,bg).toFixed(2));}
      // the switcher's second line is the same colour at opacity .85
      for(const [f,b] of [['--primary','--primary-tint'],['--ink','--surface']]){const bg=res(t,b);assert.ok(cr(mix(res(t,f),bg,.85),bg)>=need,t+' small at .85 '+f);}
      assert.ok(cr(res(t,'--warn-fill'),res(t,'--surface'))>=3,t+' dup-tick border');
    }
  });
}
