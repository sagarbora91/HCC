// W4-12: prepares (and with --assemble builds) the production (server) flavour of the debug APK — build type productionDebug,
// app id in.hcc.prototype.server, "HCC Server". The prototype APK (assembleDebug, www/ + cap sync) is not touched.
//   node scripts/build-production.mjs [--ca <HCC-Hotel-CA.cer|.pem>] [--assemble]
// 1. checks 03 Prototype/dist/production/index.html exists and is newer than the sources (npm run build:production);
// 2. copies the hotel CA PUBLIC certificate (default ../08 Production/deployment/windows/certs/HCC-Hotel-CA.cer, or
//    $env:HCC_HOTEL_CA_CERT) to android/app/src/productionDebug/res/raw/hcc_hotel_ca.pem — refuses private keys / PFX files;
// 3. copies the production web build to android/app/src/productionDebug/assets/public/index.html (overrides main's copy);
// 4. --assemble: gradlew assembleProductionDebug (set JAVA_HOME / ANDROID_HOME first, README "Prerequisites").
import {readFileSync,writeFileSync,mkdirSync,copyFileSync,statSync,readdirSync,existsSync} from 'node:fs';
import {join,dirname,resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {X509Certificate} from 'node:crypto';
import {spawnSync} from 'node:child_process';

const here=dirname(fileURLToPath(import.meta.url)),root=join(here,'..');
const proto=join(root,'..','03 Prototype');
const args=process.argv.slice(2);
const opt=n=>{const i=args.indexOf(n);return i>=0?args[i+1]:undefined;};
const fail=m=>{console.error('build-production: '+m);process.exit(1);};

// Pure checks (also used by scripts/build-production.test.mjs).
export function readHotelCa(file){
  if(!file||!existsSync(file))return {ok:false,text:`Hotel CA certificate not found: ${file}. Export it on the server PC with deployment/windows/New-HccCertificate.ps1 (certs/HCC-Hotel-CA.cer) and pass --ca <path>.`};
  if(/^\.(pfx|p12|key|jks|keystore)$/i.test(extname(file)))return {ok:false,text:`${file} is a private-key container. Use the public CA certificate (HCC-Hotel-CA.cer), never the .pfx.`};
  const buf=readFileSync(file);
  if(/PRIVATE KEY/.test(buf.toString('latin1')))return {ok:false,text:`${file} contains a private key. Use the public CA certificate only.`};
  let x;try{x=new X509Certificate(buf);}catch(e){return {ok:false,text:`${file} is not an X.509 certificate (DER .cer or PEM): ${e.message}`};}
  const warnings=[];
  if(!x.ca)warnings.push('this certificate is not a CA certificate: only a server certificate that exactly matches the server address will be trusted.');
  if(Date.parse(x.validTo)<Date.now())return {ok:false,text:`${file} expired on ${x.validTo}.`};
  return {ok:true,pem:x.toString(),subject:x.subject.replace(/\n/g,', '),validTo:x.validTo,sha256:x.fingerprint256,ca:x.ca,warnings};
}
export function isStale(built,dirs,files){
  const newest=d=>readdirSync(d,{withFileTypes:true}).reduce((m,e)=>{const p=join(d,e.name);return Math.max(m,e.isDirectory()?newest(p):statSync(p).mtimeMs);},0);
  const src=Math.max(...dirs.map(newest),...files.map(f=>statSync(f).mtimeMs));
  return statSync(built).mtimeMs<src;
}

if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  const built=join(proto,'dist','production','index.html');
  if(!existsSync(built))fail(`Missing ${built}. Run "npm run build:production" in 03 Prototype first.`);
  if(isStale(built,[join(proto,'src')],[join(proto,'build.mjs')]))fail('dist/production/index.html is older than the sources in 03 Prototype/src. Run "npm run build:production" first.');
  const caFile=resolve(opt('--ca')??process.env.HCC_HOTEL_CA_CERT??join(root,'..','08 Production','deployment','windows','certs','HCC-Hotel-CA.cer'));
  const ca=readHotelCa(caFile);if(!ca.ok)fail(ca.text);
  const flavour=join(root,'android','app','src','productionDebug');
  mkdirSync(join(flavour,'res','raw'),{recursive:true});writeFileSync(join(flavour,'res','raw','hcc_hotel_ca.pem'),ca.pem);
  console.log(`Hotel CA: ${ca.subject} · valid to ${ca.validTo}\n  SHA-256 ${ca.sha256}\n  (compare with the fingerprint printed on the server PC)`);
  for(const w of ca.warnings)console.warn('Warning: '+w);
  mkdirSync(join(flavour,'assets','public'),{recursive:true});copyFileSync(built,join(flavour,'assets','public','index.html'));
  console.log(`Copied dist/production/index.html (${Math.round(statSync(built).size/1024)} KB) to src/productionDebug/assets/public/index.html`);
  const mainAssets=join(root,'android','app','src','main','assets');
  if(!existsSync(join(mainAssets,'capacitor.plugins.json'))||!existsSync(join(mainAssets,'public','index.html'))){
    if(!existsSync(join(root,'www','index.html')))fail('Capacitor assets are missing in android/app/src/main/assets. Run the prototype steps once (03 Prototype: npm run build; 04 Android: node scripts/copy-web.mjs; npx cap sync android).');
    console.log('Running "npx cap sync android" (Capacitor runtime assets)…');
    const r=spawnSync('npx',['cap','sync','android'],{cwd:root,stdio:'inherit',shell:true});if(r.status!==0)fail('npx cap sync android failed.');
  }
  if(args.includes('--assemble')){
    const win=process.platform==='win32';
    const r=spawnSync(win?'gradlew.bat':'./gradlew',['assembleProductionDebug'],{cwd:join(root,'android'),stdio:'inherit',shell:win});
    if(r.status!==0)fail('gradle assembleProductionDebug failed.');
    console.log('APK: 04 Android/android/app/build/outputs/apk/productionDebug/app-productionDebug.apk');
  }else console.log('Next: cd android; gradlew.bat assembleProductionDebug   (or re-run with --assemble)');
}
