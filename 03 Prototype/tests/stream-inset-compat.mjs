import {readFileSync,readdirSync} from 'node:fs';
const sheets=['src/styles.css',...readdirSync('src/styles').filter(f=>f.endsWith('.css')).map(f=>'src/styles/'+f)];
export function register({test,assert}){
  test('[AC-02] bundled CSS avoids inset shorthand and explicitly anchors all overlay edges',()=>{
    const css=sheets.map(f=>readFileSync(f,'utf8')).join('\n');
    assert.doesNotMatch(css,/\binset\s*:/);
    for(const selector of ['.modal-overlay','.sidebar-overlay','.search-overlay','.exit-ask','.drawer-overlay']){
      const blocks=css.match(new RegExp(selector.replaceAll('.','\\.')+'\\s*\\{[^}]*\\}','g'))||[];
      assert.ok(blocks.some(b=>['top:0','right:0','bottom:0','left:0'].every(edge=>b.includes(edge))),selector);
    }
    assert.match(css,/\.sidebar\{[^}]*top:0;right:auto;bottom:0;left:0;/);
    assert.match(css,/\.m-avatar:before\{[^}]*top:6px;right:6px;bottom:6px;left:6px;/);
  });
}
