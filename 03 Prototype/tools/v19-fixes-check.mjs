// F2 apk19-fix · static source checks (no browser, no device). Run: node tools/v19-fixes-check.mjs
// One group per defect; device-only confirmation is listed in 05 Delivery Evidence/Acceptance/F2-apk19-fix-DONE.md.
import {readFileSync} from 'node:fs';
const rd=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const A='../04 Android/android/app/src/main/java/in/hcc/prototype/';
const plugin=rd(A+'StatusBarColorPlugin.java'),main=rd(A+'MainActivity.java'),native=rd('src/app/native.ts'),shell=rd('src/app/mobile/MobileShell.tsx'),fit=rd('src/components/fit.tsx');
let bad=0;const check=(l,ok)=>{if(!ok)bad++;console.log(`${l}: ${ok?'OK':'FAIL'}`);};
// V17-1 (insets lost): native re-dispatch + FIT sticky modal actions
check('V17-1 plugin re-requests insets after painting',/static void reapplyInsets/.test(plugin)&&/requestApplyInsets\(decor\)/.test(plugin)&&/reapplyInsets\(decor\);\s*\n\s*}/.test(plugin));
check('V17-1 MainActivity re-requests insets on configuration change',/onConfigurationChanged/.test(main)&&/reapplyInsets\(getWindow\(\)\.getDecorView\(\)\)/.test(main));
check('V17-1 FIT ModalFrame keeps a sticky .modal-actions in a scrolling modal-body',/sticky \.modal-actions/.test(fit)&&/className="modal-body" data-fit-scroll/.test(fit));
// V17-2 (resize storm)
check('V17-2 plugin skips the window-background swap for an unchanged pair',/instanceof BarBackground/.test(plugin)&&/if \(!same\)/.test(plugin));
check('V17-2 JS ignores a resize with an unchanged window size',/sizeSame/.test(native)&&/if\(!sizeSame\(\)\)fire\(\)/.test(native)&&/removeEventListener\('resize',onResize\)/.test(native));
// V17-4 (Room board date line)
check('V17-4 the status word never shrinks away; the date part ellipsises',/<b style=\{\{flexShrink:0\}\}>\{closed\?'Closed':'Open'\}<\/b>/.test(shell)&&/textOverflow:'ellipsis'/.test(shell));
// V15-4 (tab labels)
check('V15-4 tab labels do not break inside a word',/overflowWrap:'normal'/.test(shell)&&/wordBreak:'keep-all'/.test(shell)&&(shell.match(/style=\{TAB_LABEL_STYLE\}/g)||[]).length===2);
process.exit(bad?1:0);
