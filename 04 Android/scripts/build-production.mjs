// W4-12: prepares (and with --assemble builds) the production (server) flavour of the debug APK — build type productionDebug,
// app id in.hcc.prototype.server, "HCC Server". The prototype APK (assembleDebug, www/ + cap sync) is not touched.
//   node scripts/build-production.mjs [--ca <HCC-Hotel-CA.cer|.pem>] [--assemble]
// 1. checks 03 Prototype/dist/production/index.html exists and is newer than the sources (npm run build:production);
// 2. copies the hotel CA PUBLIC certificate (default ../08 Production/deployment/windows/certs/HCC-Hotel-CA.cer, or
//    $env:HCC_HOTEL_CA_CERT) to android/app/src/productionDebug/res/raw/hcc_hotel_ca.pem — refuses private keys / PFX files;
// 3. copies the production web build to android/app/src/productionDebug/assets/public/index.html (overrides main's copy);
// 4. --assemble: gradlew assembleProductionDebug (set JAVA_HOME / ANDROID_HOME first, README "Prerequisites").
//    --assemble --release: gradlew assembleProductionRelease (security review F-H02: not debuggable, release-signed with the
//    keystore from HCC_RELEASE_* / android/keystore.properties, no backup), then verifies the APK (verifyReleaseApk).
//   node scripts/build-production.mjs --verify <apk>   checks an already built release APK.
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
/** Static checks of the release configuration (F-H02, F-L10) — no Gradle needed. Returns the problems found. */
export function checkReleaseConfig(androidDir){
  const read=f=>readFileSync(join(androidDir,f),'utf8');const problems=[];
  const gradle=read('app/build.gradle'),man=read('app/src/productionRelease/AndroidManifest.xml'),
    rules=read('app/src/productionRelease/res/xml/hcc_data_extraction_rules.xml'),full=read('app/src/productionRelease/res/xml/hcc_full_backup_rules.xml'),
    paths=read('app/src/main/res/xml/file_paths.xml'),act=read('app/src/main/java/in/hcc/prototype/MainActivity.java');
  const block=(gradle.match(/\n\s*productionRelease\s*\{[\s\S]*?\n\s{8}\}/)||[''])[0];
  if(!block)problems.push('build.gradle has no productionRelease build type');
  if(!/debuggable\s+false/.test(block))problems.push('productionRelease must set debuggable false');
  if(/initWith\s+debug\b/.test(block))problems.push('productionRelease must not start from the debug build type');
  if(!/signingConfig\s+signingConfigs\.hccRelease/.test(block))problems.push('productionRelease must use the hccRelease signing config');
  if(!/HCC_RELEASE_STORE_FILE/.test(gradle)||!/keystore\.properties/.test(gradle))problems.push('signing must come from HCC_RELEASE_* variables or keystore.properties');
  if(/storePassword\s+['"]/.test(gradle))problems.push('a keystore password is written in build.gradle');
  if(!/preProductionReleaseBuild[\s\S]*checkHccReleaseSigning/.test(gradle))problems.push('the release build must refuse a missing / in-repo keystore');
  if(!/android:allowBackup="false"/.test(man)||!/tools:replace="android:allowBackup"/.test(man))problems.push('release manifest must set allowBackup="false" (tools:replace)');
  if(!/android:dataExtractionRules="@xml\/hcc_data_extraction_rules"/.test(man)||!/android:fullBackupContent="@xml\/hcc_full_backup_rules"/.test(man))problems.push('release manifest must name the backup / data-extraction rules');
  if(!/network_security_config/.test(man)||!/usesCleartextTraffic="false"/.test(man))problems.push('release manifest must keep HTTPS-only networking');
  for(const d of ['root','file','database','sharedpref','external']){
    if(rules.split(`<exclude domain="${d}" path="." />`).length!==3)problems.push(`data-extraction rules must exclude ${d} for cloud backup and device transfer`);
    if(!full.includes(`<exclude domain="${d}" path="." />`))problems.push(`full-backup rules must exclude ${d}`);
  }
  if(/<include/.test(rules+full))problems.push('backup rules must not include anything');
  if(/external-path[^>]*path="\."/.test(paths)||/root-path/.test(paths))problems.push('FileProvider must not share the whole external storage (F-L10)');
  if(!/external-path name="hcc_exports" path="Documents\/HCC\/"/.test(paths))problems.push('FileProvider must share Documents/HCC/ (the export folder)');
  if(!/setWebContentsDebuggingEnabled\(false\)/.test(act)||!/FLAG_DEBUGGABLE/.test(act))problems.push('MainActivity must switch WebView debugging off in non-debuggable builds');
  return problems;
}
/** aapt-based check of a built release APK: not debuggable, allowBackup false, app id in.hcc.server. */
export function verifyReleaseApk(apk,aapt){
  const run=a=>spawnSync(aapt,a,{encoding:'utf8',maxBuffer:64*1024*1024});
  const badging=run(['dump','badging',apk]);if(badging.status!==0)return {ok:false,problems:['aapt dump badging failed: '+(badging.stderr||badging.error)]};
  const tree=run(['dump','xmltree',apk,'AndroidManifest.xml']);if(tree.status!==0)return {ok:false,problems:['aapt dump xmltree failed: '+(tree.stderr||tree.error)]};
  return checkApkDump(badging.stdout,tree.stdout);
}
/** Pure part of verifyReleaseApk (also unit-tested). */
export function checkApkDump(badging,xmltree){
  const problems=[];const pkg=(badging.match(/package: name='([^']+)'/)||[])[1];
  if(pkg!=='in.hcc.server')problems.push(`application id is ${pkg}, expected in.hcc.server`);
  if(/application-debuggable/.test(badging))problems.push('APK is debuggable');
  if(/android:debuggable\([^)]*\)=\(type 0x12\)0xffffffff/.test(xmltree))problems.push('manifest sets android:debuggable=true');
  if(!/android:allowBackup\([^)]*\)=\(type 0x12\)0x0\b/.test(xmltree))problems.push('manifest does not set android:allowBackup=false');
  if(/android:usesCleartextTraffic\([^)]*\)=\(type 0x12\)0xffffffff/.test(xmltree))problems.push('cleartext traffic is allowed');
  return {ok:problems.length===0,problems,pkg};
}
/** Newest build-tools aapt under ANDROID_HOME / ANDROID_SDK_ROOT. */
export function findAapt(){
  const sdk=process.env.ANDROID_HOME||process.env.ANDROID_SDK_ROOT;if(!sdk)return undefined;
  const exe=process.platform==='win32'?'aapt.exe':'aapt';const bt=join(sdk,'build-tools');if(!existsSync(bt))return undefined;
  const v=readdirSync(bt).sort((a,b)=>b.localeCompare(a,undefined,{numeric:true})).find(d=>existsSync(join(bt,d,exe)));
  return v?join(bt,v,exe):undefined;
}
export function isStale(built,dirs,files){
  const newest=d=>readdirSync(d,{withFileTypes:true}).reduce((m,e)=>{const p=join(d,e.name);return Math.max(m,e.isDirectory()?newest(p):statSync(p).mtimeMs);},0);
  const src=Math.max(...dirs.map(newest),...files.map(f=>statSync(f).mtimeMs));
  return statSync(built).mtimeMs<src;
}

const verifyAndReport=apk=>{
  const aapt=findAapt();if(!aapt)fail('aapt not found: set ANDROID_HOME to the Android SDK (build-tools).');
  const r=verifyReleaseApk(apk,aapt);
  if(!r.ok)fail(`release APK check failed for ${apk}:\n  - ${r.problems.join('\n  - ')}`);
  console.log(`Release APK verified (${r.pkg}): not debuggable, allowBackup=false, no cleartext.`);
};
const main=process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url);
if(main&&args.includes('--verify')){verifyAndReport(resolve(opt('--verify')??''));process.exit(0);}
if(main){
  const release=args.includes('--release');
  if(release){const p=checkReleaseConfig(join(root,'android'));if(p.length)fail('release configuration:\n  - '+p.join('\n  - '));}
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
  const task=release?'assembleProductionRelease':'assembleProductionDebug';
  if(args.includes('--assemble')){
    const win=process.platform==='win32';
    const r=spawnSync(win?'gradlew.bat':'./gradlew',[task],{cwd:join(root,'android'),stdio:'inherit',shell:win});
    if(r.status!==0)fail(`gradle ${task} failed.`);
    if(release){verifyAndReport(join(root,'android','app','build','outputs','apk','productionRelease','app-productionRelease.apk'));
      console.log('APK: 04 Android/android/app/build/outputs/apk/productionRelease/app-productionRelease.apk');}
    else console.log('APK: 04 Android/android/app/build/outputs/apk/productionDebug/app-productionDebug.apk');
  }else console.log(`Next: cd android; gradlew.bat ${task}   (or re-run with --assemble)`);
}
