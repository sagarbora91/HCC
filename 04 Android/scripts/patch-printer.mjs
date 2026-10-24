// HCC local adaptation of @capgo/capacitor-printer 8.1.8 (bundled LICENSE: MPL-2.0; original notices retained).
// Applies only to HTML/WebView print jobs. PDF/file page sizes are left alone.
import {readFileSync,writeFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';
export function patchPrinter(source){
  const start=source.indexOf('private void createWebPrintJob(WebView webView, String name, OnPrintFinishCallback callback)');
  const end=source.indexOf('\n    private ',start+1);
  if(start<0||end<0)throw new Error('Printer source changed: review HCC A4 patch before building.');
  const section=source.slice(start,end);
  const original='new PrintAttributes.Builder().build()';
  const replacement='new PrintAttributes.Builder().setMediaSize(PrintAttributes.MediaSize.ISO_A4).build()';
  const count=s=>section.split(s).length-1;
  if(count(replacement)===2&&count(original)===0)return source;
  if(count(original)!==2||count(replacement)!==0)throw new Error('Unexpected HTML print attributes: review HCC A4 patch.');
  return source.slice(0,start)+section.split(original).join(replacement)+source.slice(end);
}
export function applyPrinterPatch(){
  const root=new URL('../node_modules/@capgo/capacitor-printer/',import.meta.url);
  const pkg=JSON.parse(readFileSync(new URL('package.json',root),'utf8'));
  if(pkg.version!=='8.1.8')throw new Error(`Review HCC A4 patch for printer ${pkg.version}; expected 8.1.8.`);
  const path=new URL('android/src/main/java/com/capgo/printer/Printer.java',root);
  const before=readFileSync(path,'utf8'),after=patchPrinter(before);
  if(after!==before)writeFileSync(path,after);
  console.log('HCC HTML printing: A4 default verified (printer 8.1.8).');
}
if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url))applyPrinterPatch();
