// W05 phone fixes (W05-1…6, AC-03, AC-04, DEC-060). Names prefixed [PF].
import {build} from 'esbuild';
import {writeFile,readFile} from 'node:fs/promises';
await writeFile('tests/.generated/pf-nav-entry.ts',"export {navFilterModules} from '../../src/app/navmap';export {navItems} from '../../src/app/nav';");
await build({entryPoints:['tests/.generated/pf-nav-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/pf-nav.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
const N=await import('./.generated/pf-nav.mjs');
export function register({test,M,assert}){
  test('[PF] W05-1 compliance calendar is in navigation for Owner and Accountant only',()=>{const s=M.seed();
    for(const u of s.users.filter(u=>u.enabled)){
      const shown=N.navFilterModules(s,N.navItems,u.id).filter(n=>M.can(s,u.id,n.p)).some(n=>n.id==='compliance');
      assert.equal(shown,u.role==='Owner'||u.role==='Accountant',u.role);}});
  test('[PF] AC-04 sheet form submit clears the dirty flag before the child handler runs (capture phase)',async()=>{
    const src=await readFile('src/app/ModalHost.tsx','utf8');
    assert.ok(/onSubmitCapture=\{\(\)=>setDirty\(false\)\}/.test(src));assert.ok(!/ onSubmit=\{\(\)=>setDirty/.test(src));
    const app=await readFile('src/app/App.tsx','utf8');
    assert.ok(/setError\(''\);dirty\.current=false;/.test(app),'run() success clears dirty');});
}
