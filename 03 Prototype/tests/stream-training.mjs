// Wave TRN: the training driver (src/app/demo/training). Acts interpreter (pure parsing), the course steps file (when present) and the
// build-flavour guarantee: the production bundle contains no part of the driver, the prototype bundle does.
import {build} from 'esbuild';
import {readFile,access} from 'node:fs/promises';
export async function register({test,assert}){
  const out=await build({entryPoints:['src/app/demo/training/acts.ts'],bundle:true,platform:'node',format:'esm',write:false,logLevel:'error'});
  const A=await import('data:text/javascript;base64,'+Buffer.from(out.outputFiles[0].text).toString('base64'));
  test('[TRN] parseAct splits kind, target and value; type and select need "<target>|<value>"',()=>{
    assert.deepEqual(A.parseAct('click:btn:Save'),{kind:'click',arg:'btn:Save',raw:'click:btn:Save'});
    const t=A.parseAct('type:field:Guest name|Rahul Deshmukh');assert.equal(t.kind,'type');assert.equal(t.arg,'field:Guest name');assert.equal(t.value,'Rahul Deshmukh');
    assert.equal(A.parseAct('select:field:Room|203 Deluxe').value,'203 Deluxe');
    assert.equal(A.parseAct('type:sel:input|a|b').value,'a|b'); // only the first bar separates
    assert.throws(()=>A.parseAct('type:field:Name'),/needs/);
    assert.throws(()=>A.parseAct('fly:away'),/unknown act/);
    assert.equal(A.parseAct('signout').kind,'signout');
  });
  test('[TRN] parseKey and parseMenuPath',()=>{
    assert.deepEqual(A.parseKey('ctrl+k'),{key:'k',ctrlKey:true,shiftKey:false,altKey:false,metaKey:false});
    assert.equal(A.parseKey('Escape').key,'Escape');assert.equal(A.parseKey('F6').ctrlKey,false);
    assert.deepEqual(A.parseMenuPath('Front Desk > Room board'),['Front Desk','Room board']);
    assert.deepEqual(A.parseMenuPath('Dashboard'),['Dashboard']);
  });
  // The course file (06 Training/hcc-web/steps.json, repo root level): every act parses, targets use known kinds, ids and shots are unique.
  const stepFiles=['../06 Training/hcc-web/steps.json','../06 Training/hcc-phone/steps.json'];
  for(const f of stepFiles){
    let raw=null;try{await access(f);raw=await readFile(f,'utf8');}catch{raw=null;}
    if(!raw)continue;
    test(`[TRN] ${f}: every act parses, every target kind is known, ids and shots are unique`,()=>{
      const kinds=new Set(['sel','aria','btn','tab','field','row','modalbtn','menutop','menuleaf','tr','text','dialog']);
      const j=JSON.parse(raw);const ids=new Set(),shots=new Set();
      for(const c of j.chapters)for(const s of c.steps){
        assert.ok(!ids.has(s.id),'duplicate id '+s.id);ids.add(s.id);assert.ok(!shots.has(s.shot),'duplicate shot '+s.shot);shots.add(s.shot);
        if(s.target)assert.ok(kinds.has(s.target.split(':')[0]),s.id+' target kind: '+s.target);
        for(const a of s.auto||[]){const p=A.parseAct(a);const t=p.kind==='type'||p.kind==='select'||p.kind==='click'||p.kind==='check'||p.kind==='uncheck'?p.arg:null;
          if(t&&t.includes(':'))assert.ok(kinds.has(t.split(':')[0]),s.id+' act target kind: '+a);}
      }
    });
  }
  // Flavours: same defines as build.mjs.
  const bundle=async flavour=>{const define={'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':`"${flavour}"`};
    const w=(await build({entryPoints:['src/app/scenarioWorker.ts'],bundle:true,write:false,format:'iife',target:['es2020'],minify:true,logLevel:'error',define})).outputFiles[0].text;
    return (await build({entryPoints:['src/main.tsx'],bundle:true,write:false,format:'iife',target:['es2020'],minify:true,logLevel:'error',define:{...define,__HCC_SCENARIO_WORKER__:JSON.stringify(w)}})).outputFiles[0].text;};
  const [proto,prod]=await Promise.all([bundle('prototype'),bundle('production')]);
  test('[TRN] production bundle contains no part of the training driver; the prototype bundle does',()=>{
    for(const m of ['TRAINING','/training/steps.json','/training/phone.json','data-tr="','target not found: ','app never became ready'])assert.ok(!prod.includes(m),'production bundle contains '+m);
    assert.ok(!/has\(["']training["']\)/.test(prod),'production bundle checks ?training');
    for(const m of ['TRAINING','/training/steps.json'])assert.ok(proto.includes(m),'prototype bundle lacks '+m);
  });
}
