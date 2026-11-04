// AF-SMALL (audit-fix wave 1): UX-05 modal dirty flag, assistant hidden in production. Names prefixed [AF-UX05] / [AF-SMALL].
import {readFileSync} from 'node:fs';
const read=f=>readFileSync(f,'utf8');
export function register({test,assert}){
  test('[AF-UX05] ModalHost has no submit-capture dirty clear; App.tsx clears dirty only after a successful run()',()=>{
    assert.doesNotMatch(read('src/app/ModalHost.tsx'),/onSubmitCapture/);
    assert.match(read('src/app/App.tsx'),/setError\(''\);dirty\.current=false;/);
  });
  test('[AF-SMALL] the demo assistant registration has a production branch that empties nav, pages and phone pages',()=>{
    const src=read('src/features/assistant/register.tsx');
    assert.match(src,/process\.env\.HCC_BUILD==='production'/);
    const P=String.raw`process\.env\.HCC_BUILD==='production'\?`;
    assert.match(src,new RegExp('pages:'+P+String.raw`\{\}`));assert.match(src,new RegExp('nav:'+P+String.raw`\[\]`));assert.match(src,new RegExp(String.raw`mobile:\{pages:`+P+String.raw`\{\}`));
  });
}
