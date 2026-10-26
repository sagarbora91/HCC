// V14-1: a sheet with unsaved input (master sheets included) asks "Discard changes?" through the app's own dialog, never window.confirm.
import {build} from 'esbuild';
import {readFileSync,readdirSync,statSync} from 'node:fs';
import {join} from 'node:path';
await build({entryPoints:['src/components/ui.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/ui-discard.mjs',external:['react','react-dom','lucide-react'],logLevel:'error'});
global.document={activeElement:null,body:{}};
const UI=await import('./.generated/ui-discard.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
delete global.document;
const read=f=>readFileSync(f,'utf8');
const walk=d=>readdirSync(d).flatMap(n=>{const p=join(d,n);return statSync(p).isDirectory()?walk(p):[p];});
export function register({test,assert}){
  test('[DISCARD] DiscardAsk is a named, described alertdialog with Keep editing / Discard buttons',()=>{
    global.document={activeElement:null,body:{}};
    const html=renderToStaticMarkup(React.createElement(UI.DiscardAsk,{onKeep(){},onDiscard(){}}));
    delete global.document;
    assert.match(html,/role="alertdialog"/);assert.match(html,/aria-modal="true"/);assert.match(html,/aria-labelledby="discard-ask-title"/);
    assert.match(html,/>Discard changes\?</);assert.match(html,/>Keep editing</);assert.match(html,/>Discard</);assert.match(html,/exit-card/);
  });
  test('[DISCARD] App closeModal and the popstate/Back path open DiscardAsk; Android Back while it is open means Keep editing; no window.confirm for sheets',()=>{
    const app=read('src/app/App.tsx');
    assert.ok(!app.includes('window.confirm'));
    assert.ok(app.includes('const closeModal=()=>{if(dirty.current){setDiscardAsk(true);return;}discardSheet();};'));
    const pop=app.slice(app.indexOf('const onPop='),app.indexOf("window.addEventListener('popstate'"));
    assert.ok(pop.includes('if(dirty.current){')&&pop.includes('setDiscardAsk(true);return;}'),'popstate with a dirty sheet opens the dialog');
    assert.ok(app.includes('if(discardAsk){setDiscardAsk(false);return;}'),'Android Back while the dialog is open = Keep editing');
    assert.ok(app.includes('<DiscardAsk onKeep={()=>setDiscardAsk(false)} onDiscard={discardSheet}/>'));
  });
  test('[DISCARD] the master form and phone master sheets never call window.confirm / confirm()',()=>{
    const files=['src/features/masters/MasterForm.tsx',...walk('src/mobile/masters'),...walk('src/features/masters')].filter(f=>/\.tsx?$/.test(f));
    for(const f of new Set(files))assert.doesNotMatch(read(f),/(window\.|[^.\w])confirm\(/,f);
    assert.ok(read('src/features/masters/MasterForm.tsx').includes('setDirty(true)')); // typing marks the sheet dirty, so closeModal asks through DiscardAsk
  });
}
