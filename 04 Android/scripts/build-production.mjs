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
//   node scripts/build-production.mjs --verify <apk> [--ca <HCC-Hotel-CA.cer> | --expect-ca-sha256 <fingerprint>]
//        [--expect-signer-sha256 <fingerprint>] [--allow-test-signer]      checks an already built release APK (A10-03):
//     aapt: app id in.hcc.server, not debuggable, allowBackup=false, no cleartext; the bundled res/raw/hcc_hotel_ca.pem is one public
//     X.509 certificate (no private key, not expired) and IS the hotel CA the build was given (SHA-256 of --ca / $env:HCC_HOTEL_CA_CERT /
//     the default path, or --expect-ca-sha256); the bundled web page carries its CSP meta (A10-05); apksigner verify -v --print-certs (from
//     the SDK build-tools: ANDROID_HOME, android/local.properties, E:\Android\Sdk; skipped with a warning when not found) verifies and
//     names the signer, which must not be the Android debug key or a throwaway / test key unless --allow-test-signer (test builds only).
//     The same checks run after --assemble --release, against the CA that build was given.
import {readFileSync,writeFileSync,mkdirSync,copyFileSync,statSync,readdirSync,existsSync} from 'node:fs';
import {join,dirname,resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {X509Certificate} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {inflateRawSync} from 'node:zlib';
import {checkCspMeta} from '../../03 Prototype/tools/csp.mjs';

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
/** Wave B1 (DEC-073): the one external entry the FileProvider may have — Capacitor 8's camera capture file
 *  (BridgeWebChromeClient.createImageFile → getExternalFilesDir(DIRECTORY_PICTURES), app-specific, never public storage). */
export const SLIP_CAPTURE_PATH='<external-files-path name="hcc_slip_capture" path="Pictures/" />';
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
  // Exactly the slip-capture entry is allowed (once); anything else external, or a wider variant of it, is refused.
  const others=paths.split(SLIP_CAPTURE_PATH);
  if(others.length>2)problems.push('FileProvider lists the slip-capture folder more than once');
  if(/external-path|root-path|external-files-path|external-cache-path/.test(others.join('')))problems.push('FileProvider must not share external or public storage (F-L10, A10-01) — only '+SLIP_CAPTURE_PATH);
  if(/cache-path[^>]*path="\.?\/?"/.test(paths))problems.push('FileProvider must not share the whole app cache (A10-01)');
  if(!/cache-path name="hcc_exports" path="exports\/"/.test(paths))problems.push('FileProvider must share only the cache exports/ folder (A10-01)');
  if(!/setWebContentsDebuggingEnabled\(false\)/.test(act)||!/FLAG_DEBUGGABLE/.test(act))problems.push('MainActivity must switch WebView debugging off in non-debuggable builds');
  problems.push(...checkNetworkConfig(androidDir));
  return problems;
}
/** A10-04: the network security config shared by productionDebug and productionRelease (src/productionDebug/res/xml): HTTPS only, trust
 *  anchors the bundled hotel CA plus (for a hotel that buys a public certificate) the system CAs and nothing else — never user-installed
 *  CAs, never another bundled CA, no pin overrides — and the productionDebug manifest applies it. Returns the problems found. */
export function checkNetworkConfig(androidDir){
  const read=f=>readFileSync(join(androidDir,f),'utf8');const problems=[];
  const nsc=read('app/src/productionDebug/res/xml/network_security_config.xml').replace(/<!--[\s\S]*?-->/g,''),man=read('app/src/productionDebug/AndroidManifest.xml');
  const base=(nsc.match(/<base-config\b[\s\S]*?<\/base-config>/)||[''])[0];
  if(!/^<base-config\s+cleartextTrafficPermitted="false"\s*>/.test(base))problems.push('network_security_config.xml: base-config must refuse cleartext (cleartextTrafficPermitted="false")');
  if(/cleartextTrafficPermitted="true"/.test(nsc))problems.push('network_security_config.xml must not allow cleartext for any domain');
  const anchors=[...nsc.matchAll(/<certificates\b[^>]*\bsrc="([^"]*)"/g)].map(m=>m[1]);
  if(anchors.includes('user'))problems.push('network_security_config.xml must not trust user-installed CAs (A10-04)');
  const other=anchors.filter(a=>!['system','user','@raw/hcc_hotel_ca'].includes(a));
  if(other.length)problems.push(`network_security_config.xml may trust only the bundled hotel CA (and the system CAs), found: ${other.join(', ')}`);
  if(!/<certificates\s+src="@raw\/hcc_hotel_ca"\s*\/>/.test(base))problems.push('network_security_config.xml: base-config must trust the bundled hotel CA (@raw/hcc_hotel_ca)');
  if(/overridePins\s*=\s*"true"/.test(nsc))problems.push('network_security_config.xml must not let a trust anchor override pins');
  if(!/android:networkSecurityConfig="@xml\/network_security_config"/.test(man)||!/android:usesCleartextTraffic="false"/.test(man))problems.push('productionDebug manifest must keep HTTPS-only networking (network_security_config, usesCleartextTraffic="false")');
  return problems;
}
/** Check of a built release APK (aapt, the APK's own files, apksigner): see checkReleaseApk. `expect` = {expectCaSha256,
 *  expectSignerSha256, allowTestSigner}; `tools.apksigner` undefined = not found (the signer check is skipped with a warning). */
export function verifyReleaseApk(apk,expect={},tools={aapt:findAapt(),apksigner:findApksigner()}){
  const {aapt,apksigner}=tools;
  if(!aapt)return {ok:false,problems:['aapt not found: set ANDROID_HOME to the Android SDK (build-tools).'],warnings:[]};
  const run=a=>spawnSync(aapt,a,{encoding:'utf8',maxBuffer:256*1024*1024});
  const badging=run(['dump','badging',apk]);if(badging.status!==0)return {ok:false,problems:['aapt dump badging failed: '+(badging.stderr||badging.error)],warnings:[]};
  const tree=run(['dump','xmltree',apk,'AndroidManifest.xml']);if(tree.status!==0)return {ok:false,problems:['aapt dump xmltree failed: '+(tree.stderr||tree.error)],warnings:[]};
  const res=run(['dump','--values','resources',apk]);
  const zip=readFileSync(apk);
  let signer;
  if(apksigner){const r=runApksigner(apksigner,apk);signer={status:r.status,out:r.stdout??'',err:(r.stderr??'')+(r.error?String(r.error.message):''),error:!!r.error};}
  return checkReleaseApk({badging:badging.stdout,xmltree:tree.stdout,resources:res.status===0?res.stdout:'',entry:n=>readZipEntry(zip,n),apksigner:signer},expect);
}
/** A10-03: everything --verify checks, on the tool outputs and the APK's files (pure; unit-tested on canned outputs).
 *  parts: {badging, xmltree, resources (aapt dump --values resources), entry(name) → Buffer|undefined, apksigner: {status,out,err,error}|undefined}. */
export function checkReleaseApk({badging,xmltree,resources,entry,apksigner},{expectCaSha256,expectSignerSha256,allowTestSigner=false}={}){
  const dump=checkApkDump(badging,xmltree),problems=[...dump.problems],warnings=[];
  const caPath=hotelCaPath(resources??'')??'res/raw/hcc_hotel_ca.pem';
  const ca=checkBundledCa(entry(caPath),expectCaSha256);problems.push(...ca.problems);warnings.push(...ca.warnings);
  const page=entry('assets/public/index.html');
  if(!page)problems.push('the APK has no web page (assets/public/index.html)');
  else problems.push(...checkCspMeta(page.toString('utf8')).map(p=>'bundled web page: '+p));
  let signer;
  if(!apksigner)warnings.push('apksigner not found under the Android SDK build-tools (ANDROID_HOME / ANDROID_SDK_ROOT, android/local.properties, E:\\Android\\Sdk): the signer was NOT checked. Run "apksigner verify -v --print-certs <apk>" by hand.');
  else if(apksigner.error)problems.push(`could not run apksigner (${apksigner.err.trim()}): set JAVA_HOME to a JDK 21 (README "Prerequisites")`);
  else if(apksigner.status!==0)problems.push(`apksigner verify failed (exit ${apksigner.status}): ${(apksigner.err+apksigner.out).split(/\r?\n/).filter(l=>l.trim()&&!/^WARNING:/.test(l)).slice(-4).join(' | ')}`);
  else{signer=checkSigner(apksigner.out,{allowTestSigner,expectSignerSha256});problems.push(...signer.problems);warnings.push(...signer.warnings);}
  return {ok:problems.length===0,problems,warnings,pkg:dump.pkg,ca,caPath,signer};
}
/** A10-03: path of the raw/hcc_hotel_ca resource inside the APK, from `aapt dump --values resources` (a release build shortens resource
 *  paths, e.g. res/-M.pem instead of res/raw/hcc_hotel_ca.pem). Also reads aapt2's "(file) <path>" form. undefined when absent. */
export function hotelCaPath(resources){
  const lines=String(resources).split(/\r?\n/);
  for(let i=0;i<lines.length;i++){
    if(!/^\s*resource 0x[0-9a-f]+ \S*:?raw\/hcc_hotel_ca\b/i.test(lines[i]))continue;
    for(let j=i+1;j<Math.min(lines.length,i+4);j++){
      const m=lines[j].match(/^\s*\(string8\) "([^"]+)"/)||lines[j].match(/\(file\) (\S+)/);if(m)return m[1];
      if(/^\s*(spec )?resource 0x/.test(lines[j]))break;
    }
  }
  return undefined;
}
/** A10-03: one zip (APK) entry's bytes, or undefined when there is no such entry. Stored and deflated entries (all an APK uses), no zip64. */
export function readZipEntry(zip,name){
  let eocd=-1;
  for(let i=zip.length-22;i>=Math.max(0,zip.length-22-0xffff);i--)if(zip.readUInt32LE(i)===0x06054b50){eocd=i;break;}
  if(eocd<0)throw new Error('not a zip / APK file (no end-of-central-directory record)');
  const count=zip.readUInt16LE(eocd+10);let p=zip.readUInt32LE(eocd+16);
  for(let k=0;k<count;k++){
    if(zip.readUInt32LE(p)!==0x02014b50)throw new Error('damaged zip central directory');
    const method=zip.readUInt16LE(p+10),size=zip.readUInt32LE(p+20),nameLen=zip.readUInt16LE(p+28),extraLen=zip.readUInt16LE(p+30),commentLen=zip.readUInt16LE(p+32),local=zip.readUInt32LE(p+42);
    if(zip.toString('utf8',p+46,p+46+nameLen)===name){
      if(zip.readUInt32LE(local)!==0x04034b50)throw new Error('damaged zip local header for '+name);
      const start=local+30+zip.readUInt16LE(local+26)+zip.readUInt16LE(local+28),data=zip.subarray(start,start+size);
      if(method===0)return Buffer.from(data);
      if(method===8)return inflateRawSync(data);
      throw new Error(`unsupported zip compression method ${method} for ${name}`);
    }
    p+=46+nameLen+extraLen+commentLen;
  }
  return undefined;
}
/** SHA-256 fingerprints compare as 64 hex digits (colons, spaces and case ignored); '' when the value is not one. */
export const normSha256=v=>{const h=String(v??'').replace(/[\s:]/g,'').toUpperCase();return /^[0-9A-F]{64}$/.test(h)?h:'';};
const colons=h=>h.match(/../g).join(':');
/** A10-03: the hotel CA bundled in the APK must be exactly one public X.509 certificate (no private key), not expired, and the CA the
 *  build was given (SHA-256 of the certificate, the same fingerprint the build prints). */
export function checkBundledCa(buf,expectSha256,now=Date.now()){
  if(!buf)return {ok:false,problems:['the APK has no bundled hotel CA (res/raw/hcc_hotel_ca.pem): build it with scripts/build-production.mjs --ca <HCC-Hotel-CA.cer>'],warnings:[]};
  const problems=[],warnings=[],text=buf.toString('latin1');
  if(/PRIVATE KEY/.test(text))problems.push('the bundled hotel CA file contains a PRIVATE KEY: rebuild from the public HCC-Hotel-CA.cer and treat that key as exposed');
  const pems=(text.match(/-----BEGIN CERTIFICATE-----/g)||[]).length;
  if(pems!==1)problems.push(`the bundled hotel CA file must hold exactly one PEM certificate (found ${pems})`);
  let x;try{x=new X509Certificate(buf);}catch(e){problems.push('the bundled hotel CA is not an X.509 certificate: '+e.message);return {ok:false,problems,warnings};}
  const subject=x.subject.replace(/\n/g,', ');
  if(!x.ca)warnings.push(`the bundled hotel CA (${subject}) is not a CA certificate: only a server certificate that exactly matches it will be trusted`);
  if(Date.parse(x.validTo)<now)problems.push(`the bundled hotel CA (${subject}) expired on ${x.validTo}`);
  const want=normSha256(expectSha256);
  if(!want)problems.push(expectSha256?`--expect-ca-sha256 "${expectSha256}" is not a SHA-256 fingerprint`:'no expected hotel CA: pass --ca <HCC-Hotel-CA.cer> (or $env:HCC_HOTEL_CA_CERT) or --expect-ca-sha256 <fingerprint> so the bundled CA can be compared');
  else if(normSha256(x.fingerprint256)!==want)problems.push(`the bundled hotel CA (${subject}, SHA-256 ${x.fingerprint256}) is not the expected hotel CA (SHA-256 ${colons(want)}): a stale or wrong res/raw/hcc_hotel_ca.pem was packaged. Rebuild with build-production.mjs --ca <the hotel CA>`);
  return {ok:problems.length===0,problems,warnings,subject,sha256:x.fingerprint256,validTo:x.validTo};
}
/** A10-03: signers from `apksigner verify --print-certs` output. */
export function parseApksigner(out){
  const text=String(out??''),signers=[];
  for(const m of text.matchAll(/^Signer #(\d+) certificate DN: (.+?)\s*$/gm))
    signers.push({dn:m[2],sha256:((text.match(new RegExp(`^Signer #${m[1]} certificate SHA-256 digest: ([0-9a-fA-F]+)\\s*$`,'m'))||[])[1]||'').toLowerCase()});
  return {verifies:/^Verifies\s*$/m.test(text)&&!/DOES NOT VERIFY/.test(text),schemes:[...text.matchAll(/^Verified using (v[\d.]+) scheme[^:]*: true\s*$/gm)].map(m=>m[1]),signers};
}
/** Distinguished names of keys that must never sign the APK handed to the hotel: the Android debug key and throwaway / test keys. */
export const TEST_SIGNER=/\bdebug\b|throwaway|\btest\b|\bdummy\b|\bsample\b|\bexample\b|do not use/i;
/** A10-03: apksigner must verify the APK, and the signer must not be a debug / throwaway key (unless allowTestSigner, for a test
 *  build only); with expectSignerSha256 the signer must be that release key. */
export function checkSigner(out,{allowTestSigner=false,expectSignerSha256}={}){
  const r=parseApksigner(out),problems=[],warnings=[];
  if(!r.verifies)problems.push('apksigner: the APK signature does NOT verify');
  if(!r.signers.length)problems.push('apksigner printed no signer certificate');
  for(const s of r.signers)if(TEST_SIGNER.test(s.dn)){
    if(allowTestSigner)warnings.push(`signed with a debug / throwaway key (${s.dn}), allowed by --allow-test-signer: never hand this APK to the hotel`);
    else problems.push(`signed with a debug / throwaway key (${s.dn}): the APK for the hotel must be signed with the hotel's release keystore (README "Owner: create the release keystore"); --allow-test-signer only for a test build`);
  }
  const want=normSha256(expectSignerSha256);
  if(expectSignerSha256&&!want)problems.push(`--expect-signer-sha256 "${expectSignerSha256}" is not a SHA-256 fingerprint`);
  else if(want&&!r.signers.some(s=>normSha256(s.sha256)===want))problems.push(`the signer (SHA-256 ${r.signers.map(s=>s.sha256).join(', ')||'none'}) is not the expected release key (${want.toLowerCase()})`);
  return {ok:problems.length===0,problems,warnings,signers:r.signers,schemes:r.schemes};
}
/** Runs `apksigner verify -v --print-certs <apk>` (-v prints "Verifies" and the schemes; apksigner.bat on Windows goes through cmd, so
 *  both paths are quoted). */
export function runApksigner(apksigner,apk){
  const opts={encoding:'utf8',maxBuffer:16*1024*1024};
  if(/\.(bat|cmd)$/i.test(apksigner))return spawnSync(`"${apksigner}" verify -v --print-certs "${apk}"`,{...opts,shell:true});
  return spawnSync(apksigner,['verify','-v','--print-certs',apk],opts);
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
/** A stale checkout (no `npm ci`) silently drops @capacitor/preferences from the gradle files at `cap sync` and the APK then cannot keep its sign-in. Returns the problem, or ''. */
export function checkCapacitorPlugins(androidRoot){
  return existsSync(join(androidRoot,'node_modules','@capacitor','preferences'))?'':`node_modules/@capacitor/preferences is missing in ${androidRoot}. Run "npm ci" in 04 Android first (every build, after every checkout): without it "cap sync" silently drops the Preferences plugin from the APK.`;
}
/** Android SDK folders to search, in order: ANDROID_HOME, ANDROID_SDK_ROOT, sdk.dir of android/local.properties, then (Windows)
 *  E:\Android\Sdk (this PC) and %LOCALAPPDATA%\Android\Sdk (Android Studio's default). */
export function sdkCandidates(env=process.env,androidDir=join(root,'android'),win=process.platform==='win32'){
  const out=[env.ANDROID_HOME,env.ANDROID_SDK_ROOT];
  try{const m=readFileSync(join(androidDir,'local.properties'),'utf8').match(/^\s*sdk\.dir\s*=\s*(.+?)\s*$/m);if(m)out.push(m[1].replace(/\\(.)/g,'$1'));}catch{/* no local.properties */}
  if(win){out.push('E:\\Android\\Sdk');if(env.LOCALAPPDATA)out.push(join(env.LOCALAPPDATA,'Android','Sdk'));}
  return [...new Set(out.filter(Boolean))];
}
/** Newest build-tools copy of a tool (the first of `names` found) in the first SDK folder that has one. */
export function findBuildTool(names,sdks=sdkCandidates()){
  for(const sdk of sdks){
    const bt=join(sdk,'build-tools');if(!existsSync(bt))continue;
    for(const v of readdirSync(bt).sort((a,b)=>b.localeCompare(a,undefined,{numeric:true})))for(const n of names)if(existsSync(join(bt,v,n)))return join(bt,v,n);
  }
  return undefined;
}
/** Newest build-tools aapt / apksigner (A10-03). */
export const findAapt=sdks=>findBuildTool(process.platform==='win32'?['aapt.exe']:['aapt'],sdks);
export const findApksigner=sdks=>findBuildTool(process.platform==='win32'?['apksigner.bat']:['apksigner'],sdks);
export function isStale(built,dirs,files){
  const newest=d=>readdirSync(d,{withFileTypes:true}).reduce((m,e)=>{const p=join(d,e.name);return Math.max(m,e.isDirectory()?newest(p):statSync(p).mtimeMs);},0);
  const src=Math.max(...dirs.map(newest),...files.map(f=>statSync(f).mtimeMs));
  return statSync(built).mtimeMs<src;
}

const verifyAndReport=(apk,expect)=>{
  const r=verifyReleaseApk(apk,expect);
  for(const w of r.warnings)console.warn('Warning: '+w);
  if(!r.ok)fail(`release APK check failed for ${apk}:\n  - ${r.problems.join('\n  - ')}`);
  console.log(`Release APK verified (${r.pkg}): not debuggable, allowBackup=false, no cleartext.`);
  console.log(`  Hotel CA (${r.caPath}): ${r.ca.subject} · valid to ${r.ca.validTo}\n    SHA-256 ${r.ca.sha256} = the expected hotel CA`);
  console.log('  Web page: Content-Security-Policy meta present; inline script and style allowed by hash only.');
  if(r.signer)for(const s of r.signer.signers)console.log(`  Signer: ${s.dn} (${r.signer.schemes.join(', ')||'?'} verified)\n    SHA-256 ${s.sha256}   every later APK must show this same signer (pin it with --expect-signer-sha256)`);
  else console.log('  Signer: NOT checked (apksigner not found).');
};
const caFile=()=>resolve(opt('--ca')??process.env.HCC_HOTEL_CA_CERT??join(root,'..','08 Production','deployment','windows','certs','HCC-Hotel-CA.cer'));
const signerExpect=()=>({expectSignerSha256:opt('--expect-signer-sha256'),allowTestSigner:args.includes('--allow-test-signer')});
const main=process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url);
if(main&&args.includes('--verify')){
  // The CA to compare with: --expect-ca-sha256, else the certificate of --ca / $env:HCC_HOTEL_CA_CERT / the default path (when present).
  let expectCaSha256=opt('--expect-ca-sha256');
  if(!expectCaSha256&&(opt('--ca')||process.env.HCC_HOTEL_CA_CERT||existsSync(caFile()))){const c=readHotelCa(caFile());if(!c.ok)fail(c.text);expectCaSha256=c.sha256;}
  verifyAndReport(resolve(opt('--verify')??''),{expectCaSha256,...signerExpect()});process.exit(0);
}
if(main){
  const npmProblem=checkCapacitorPlugins(root);if(npmProblem)fail(npmProblem);
  const release=args.includes('--release');
  {const p=release?checkReleaseConfig(join(root,'android')):checkNetworkConfig(join(root,'android'));if(p.length)fail((release?'release':'network')+' configuration:\n  - '+p.join('\n  - '));}
  const built=join(proto,'dist','production','index.html');
  if(!existsSync(built))fail(`Missing ${built}. Run "npm run build:production" in 03 Prototype first.`);
  if(isStale(built,[join(proto,'src')],[join(proto,'build.mjs'),join(proto,'tools','csp.mjs')]))fail('dist/production/index.html is older than the sources in 03 Prototype/src. Run "npm run build:production" first.');
  const csp=checkCspMeta(readFileSync(built,'utf8'));if(csp.length)fail(`dist/production/index.html (A10-05): ${csp.join('; ')}. Run "npm run build:production" in 03 Prototype.`);
  const ca=readHotelCa(caFile());if(!ca.ok)fail(ca.text);
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
    // V19-4: run Gradle in android/ by its absolute path (cmd.exe does not always search the working folder, and the
    // folder name "04 Android" has a space, so the path is quoted for the shell).
    const androidDir=join(root,'android'),gw=join(androidDir,win?'gradlew.bat':'gradlew');
    const r=spawnSync(win?`"${gw}"`:gw,[task],{cwd:androidDir,stdio:'inherit',shell:win});
    if(r.status!==0)fail(`gradle ${task} failed.`);
    if(release){verifyAndReport(join(root,'android','app','build','outputs','apk','productionRelease','app-productionRelease.apk'),{expectCaSha256:ca.sha256,...signerExpect()});
      console.log('APK: 04 Android/android/app/build/outputs/apk/productionRelease/app-productionRelease.apk');}
    else console.log('APK: 04 Android/android/app/build/outputs/apk/productionDebug/app-productionDebug.apk');
  }else console.log(`Next: cd android; gradlew.bat ${task}   (or re-run with --assemble)`);
}
