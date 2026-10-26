// Checks for scripts/build-production.mjs (W4-12): the hotel CA file is a public X.509 certificate, never a private key.
// The valid-certificate case needs openssl on PATH (Git for Windows ships it) and is skipped otherwise.
import assert from 'node:assert/strict';
import {mkdtempSync,writeFileSync,readFileSync,rmSync,existsSync,mkdirSync,cpSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
import {deflateRawSync,crc32} from 'node:zlib';
import {readHotelCa,checkReleaseConfig,checkNetworkConfig,checkApkDump,checkCapacitorPlugins,SLIP_CAPTURE_PATH,
  checkReleaseApk,checkBundledCa,checkSigner,parseApksigner,hotelCaPath,readZipEntry,normSha256,sdkCandidates,findBuildTool} from './build-production.mjs';
import {addCspMeta} from '../../03 Prototype/tools/csp.mjs';
const androidDir=join(dirname(fileURLToPath(import.meta.url)),'..','android');

// ── A10-03 canned tool output (apksigner 36.0.0 -v --print-certs and aapt dump --values resources, as captured by the audit in
//    05 Delivery Evidence/Audit/evidence/A10/{release,server}-apksigner.txt and server-resources.txt) ──
const JAVA_WARN='WARNING: A restricted method in java.lang.System has been called\nWARNING: Use --enable-native-access=ALL-UNNAMED to avoid a warning for callers in this module\n\n';
const signerOut=(dn,sha)=>['Verifies','Verified using v1 scheme (JAR signing): false','Verified using v2 scheme (APK Signature Scheme v2): true',
  'Verified using v3 scheme (APK Signature Scheme v3): false','Verified using v3.1 scheme (APK Signature Scheme v3.1): false','Verified using v4 scheme (APK Signature Scheme v4): false',
  'Verified for SourceStamp: false','Number of signers: 1',`Signer #1 certificate DN: ${dn}`,`Signer #1 certificate SHA-256 digest: ${sha}`,
  'Signer #1 certificate SHA-1 digest: 5269abc88d9b7af0dd529eec9a70d298798cd337','Signer #1 key algorithm: RSA','Signer #1 key size (bits): 2048',''].join('\r\n');
const APKSIGNER_THROWAWAY=JAVA_WARN+signerOut('CN=APK12 Throwaway','3aa410949a17b66e306769999af94e414a5030ffb64ee2a02c5bd5740ba3a2ba');
const APKSIGNER_DEBUG=JAVA_WARN+signerOut('C=US, O=Android, CN=Android Debug','3c49e759417d2294fe6ab2570ea6b776616d62b8e6d984b5fa3fb7e9c61fc8f0');
const HOTEL_SIGNER_SHA='b7e1c0d2a4f6081a3c5e7092b4d6f8a1c3e5072941b6d8f0a2c4e6081b3d5f70';
const APKSIGNER_HOTEL=signerOut('CN=Hotel Control Centre, O=Hotel Saagar Residency, C=IN',HOTEL_SIGNER_SHA);
const RES_DEBUG=['    type 12 configCount=1 entryCount=1','      spec resource 0x7f0d0000 in.hcc.prototype.server:raw/hcc_hotel_ca: flags=0x00000000','      config (default):',
  '        resource 0x7f0d0000 in.hcc.prototype.server:raw/hcc_hotel_ca: t=0x03 d=0x00000195 (s=0x0008 r=0x00)','          (string8) "res/raw/hcc_hotel_ca.pem"',
  '    type 13 configCount=86 entryCount=42','      spec resource 0x7f0e0000 in.hcc.prototype.server:string/abc_action_bar_home_description: flags=0x00000004'].join('\n');
const RES_RELEASE=RES_DEBUG.replaceAll('in.hcc.prototype.server','in.hcc.server').replace('d=0x00000195','d=0x00000000').replace('"res/raw/hcc_hotel_ca.pem"','"res/-M.pem"');
/** Minimal zip writer (local headers + central directory + end record) for readZipEntry. */
function makeZip(entries){
  const locals=[],central=[];let off=0;
  for(const {name,data,deflate} of entries){
    const body=deflate?deflateRawSync(data):data,n=Buffer.from(name),crc=crc32(data);
    const lh=Buffer.alloc(30);lh.writeUInt32LE(0x04034b50,0);lh.writeUInt16LE(20,4);lh.writeUInt16LE(deflate?8:0,8);lh.writeUInt32LE(crc,14);lh.writeUInt32LE(body.length,18);lh.writeUInt32LE(data.length,22);lh.writeUInt16LE(n.length,26);
    const ch=Buffer.alloc(46);ch.writeUInt32LE(0x02014b50,0);ch.writeUInt16LE(20,4);ch.writeUInt16LE(20,6);ch.writeUInt16LE(deflate?8:0,10);ch.writeUInt32LE(crc,16);ch.writeUInt32LE(body.length,20);ch.writeUInt32LE(data.length,24);ch.writeUInt16LE(n.length,28);ch.writeUInt32LE(off,42);
    locals.push(lh,n,body);central.push(ch,n);off+=30+n.length+body.length;
  }
  const cd=Buffer.concat(central),end=Buffer.alloc(22);end.writeUInt32LE(0x06054b50,0);end.writeUInt16LE(entries.length,8);end.writeUInt16LE(entries.length,10);end.writeUInt32LE(cd.length,12);end.writeUInt32LE(off,16);
  return Buffer.concat([...locals,cd,end]);
}

const dir=mkdtempSync(join(tmpdir(),'hcc-ca-'));let n=0;
const ok=(name,fn)=>{fn();n++;console.log('PASS',name);};
try{
  ok('missing file is refused with the export hint',()=>{const r=readHotelCa(join(dir,'nope.cer'));assert.equal(r.ok,false);assert.match(r.text,/New-HccCertificate/);});
  ok('a .pfx is refused',()=>{const f=join(dir,'ca.pfx');writeFileSync(f,'x');assert.match(readHotelCa(f).text,/private-key container/);});
  ok('a PEM with a private key is refused',()=>{const f=join(dir,'ca.pem');writeFileSync(f,'-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n');assert.match(readHotelCa(f).text,/contains a private key/);});
  ok('garbage is refused',()=>{const f=join(dir,'x.cer');writeFileSync(f,'not a cert');assert.match(readHotelCa(f).text,/not an X\.509 certificate/);});
  const env={...process.env,MSYS_NO_PATHCONV:'1'};
  const gen=spawnSync('openssl',['req','-x509','-newkey','rsa:2048','-nodes','-keyout',join(dir,'k.key'),'-out',join(dir,'ca.crt'),'-days','30','-subj','/CN=HCC Test CA',
    '-addext','basicConstraints=critical,CA:TRUE'],{env,encoding:'utf8'});
  if(gen.status===0&&existsSync(join(dir,'ca.crt'))){
    spawnSync('openssl',['x509','-in',join(dir,'ca.crt'),'-outform','der','-out',join(dir,'ca.cer')],{env});
    ok('DER .cer and PEM CA certificates are accepted and re-emitted as PEM (no key)',()=>{
      for(const f of ['ca.crt','ca.cer']){const r=readHotelCa(join(dir,f));assert.equal(r.ok,true,r.text);assert.equal(r.ca,true);assert.match(r.pem,/^-----BEGIN CERTIFICATE-----/);
        assert.doesNotMatch(r.pem,/PRIVATE/);assert.match(r.subject,/HCC Test CA/);assert.match(r.sha256,/^[0-9A-F:]{95}$/);}
    });
    // ── A10-03: the hotel CA inside the APK ──
    const hotel=readHotelCa(join(dir,'ca.crt')),pem=Buffer.from(hotel.pem);
    spawnSync('openssl',['req','-x509','-newkey','rsa:2048','-nodes','-keyout',join(dir,'k2.key'),'-out',join(dir,'other.crt'),'-days','30','-subj','/CN=Stale Test CA','-addext','basicConstraints=critical,CA:TRUE'],{env});
    const other=readFileSync(join(dir,'other.crt'));
    ok('[A10-03] bundled CA: the CA the build was given passes (fingerprint with or without colons, any case); a stale / other CA, no expectation, a key, two certificates, garbage, an expired CA and no file fail',()=>{
      for(const want of [hotel.sha256,hotel.sha256.replace(/:/g,'').toLowerCase()]){const r=checkBundledCa(pem,want);assert.deepEqual(r.problems,[]);assert.equal(r.sha256,hotel.sha256);assert.match(r.subject,/HCC Test CA/);}
      assert.match(checkBundledCa(other,hotel.sha256).problems.join(),/Stale Test CA.*is not the expected hotel CA.*stale or wrong res\/raw\/hcc_hotel_ca\.pem/);
      assert.match(checkBundledCa(pem,undefined).problems.join(),/no expected hotel CA: pass --ca/);
      assert.match(checkBundledCa(pem,'AEB0CC5C').problems.join(),/not a SHA-256 fingerprint/);
      assert.match(checkBundledCa(Buffer.concat([pem,readFileSync(join(dir,'k.key'))]),hotel.sha256).problems.join(),/PRIVATE KEY/);
      assert.match(checkBundledCa(Buffer.concat([pem,other]),hotel.sha256).problems.join(),/exactly one PEM certificate \(found 2\)/);
      assert.match(checkBundledCa(Buffer.from('-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----\n'),hotel.sha256).problems.join(),/not an X\.509 certificate/);
      assert.match(checkBundledCa(pem,hotel.sha256,Date.now()+400*864e5).problems.join(),/expired on/);
      assert.match(checkBundledCa(undefined,hotel.sha256).problems.join(),/no bundled hotel CA/);
    });
    ok('[A10-03] release APK check (canned aapt / apksigner output + the APK files): a hotel-signed APK with the expected CA and a CSP page passes; each fault is reported',()=>{
      const good=["package: name='in.hcc.server' versionCode='14' versionName='0.1.0-proto-server'",''].join('\n');
      const tree=['E: application (line=10)','  A: android:allowBackup(0x01010280)=(type 0x12)0x0','  A: android:usesCleartextTraffic(0x010104ec)=(type 0x12)0x0',''].join('\n');
      const page=Buffer.from(addCspMeta('<!doctype html><html lang="en"><head><meta charset="UTF-8"><style>b{}</style></head><body><script>x()</script></body></html>'));
      const files=over=>{const f={'res/-M.pem':pem,'assets/public/index.html':page,...over};return n=>f[n];};
      const parts=(o={})=>({badging:good,xmltree:tree,resources:RES_RELEASE,entry:files(),apksigner:{status:0,out:APKSIGNER_HOTEL,err:'',error:false},...o});
      const pass=checkReleaseApk(parts(),{expectCaSha256:hotel.sha256});
      assert.deepEqual([pass.ok,pass.problems,pass.warnings,pass.caPath,pass.signer.signers[0].sha256],[true,[],[],'res/-M.pem',HOTEL_SIGNER_SHA]);
      assert.equal(checkReleaseApk(parts(),{expectCaSha256:hotel.sha256,expectSignerSha256:HOTEL_SIGNER_SHA.toUpperCase()}).ok,true,'pinned signer');
      const bad=(o,e={},re)=>{const r=checkReleaseApk(parts(o),{expectCaSha256:hotel.sha256,...e});assert.equal(r.ok,false);assert.match(r.problems.join(' | '),re);return r;};
      bad({entry:files({'res/-M.pem':other})},{},/not the expected hotel CA/);
      bad({resources:''},{},/no bundled hotel CA/);                                      // the path cannot be resolved and res/raw/… is absent
      bad({entry:files({'assets/public/index.html':Buffer.from('<html><head><meta charset="UTF-8"></head><body><script>x()</script></body></html>')})},{},/bundled web page: the page has no Content-Security-Policy meta/);
      bad({entry:files({'assets/public/index.html':undefined})},{},/no web page/);
      bad({apksigner:{status:0,out:APKSIGNER_THROWAWAY,err:'',error:false}},{},/debug \/ throwaway key \(CN=APK12 Throwaway\)/);
      bad({apksigner:{status:0,out:APKSIGNER_DEBUG,err:'',error:false}},{},/CN=Android Debug/);
      bad({},{expectSignerSha256:'3aa410949a17b66e306769999af94e414a5030ffb64ee2a02c5bd5740ba3a2ba'},/not the expected release key/);
      bad({apksigner:{status:1,out:'',err:JAVA_WARN+'DOES NOT VERIFY\nERROR: APK Signature Scheme v2 signer #1: APK integrity check failed. CHUNKED_SHA256 digest mismatch.\n',error:false}},{},/apksigner verify failed \(exit 1\): DOES NOT VERIFY \| ERROR: APK Signature Scheme v2/);
      bad({apksigner:{status:null,out:'',err:'spawnSync java ENOENT',error:true}},{},/could not run apksigner/);
      // --allow-test-signer turns the throwaway signer into a warning; without apksigner the signer is skipped with a warning.
      const test=checkReleaseApk(parts({apksigner:{status:0,out:APKSIGNER_THROWAWAY,err:'',error:false}}),{expectCaSha256:hotel.sha256,allowTestSigner:true});
      assert.equal(test.ok,true);assert.match(test.warnings.join(),/allowed by --allow-test-signer: never hand this APK to the hotel/);
      const none=checkReleaseApk(parts({apksigner:undefined}),{expectCaSha256:hotel.sha256});
      assert.equal(none.ok,true);assert.match(none.warnings.join(),/apksigner not found .*the signer was NOT checked/);
      // The manifest checks still apply (a debug APK fails on them as before).
      bad({badging:good.replace('in.hcc.server','in.hcc.prototype.server')+'application-debuggable\n'},{},/expected in\.hcc\.server.*\| APK is debuggable/);
    });
  }else console.log('SKIP valid-certificate case (openssl not available)');

  // ── Security review F-H02 / F-L10: production release configuration ──
  ok('release configuration as shipped: not debuggable, hccRelease signing from env / keystore.properties, no backup, narrow FileProvider, WebView debugging off',()=>{
    assert.deepEqual(checkReleaseConfig(androidDir),[]);
    const paths=readFileSync(join(androidDir,'app/src/main/res/xml/file_paths.xml'),'utf8');
    assert.equal(paths.split(SLIP_CAPTURE_PATH).length,2,'the slip-capture folder (Capacitor 8 camera capture) is shared exactly once');
  });
  const files=['app/build.gradle','app/src/productionRelease/AndroidManifest.xml','app/src/productionRelease/res/xml/hcc_data_extraction_rules.xml',
    'app/src/productionRelease/res/xml/hcc_full_backup_rules.xml','app/src/main/res/xml/file_paths.xml','app/src/main/java/in/hcc/prototype/MainActivity.java',
    'app/src/productionDebug/res/xml/network_security_config.xml','app/src/productionDebug/AndroidManifest.xml'];
  const mutated=(file,from,to)=>{const d=join(dir,'android-'+Math.random().toString(36).slice(2));for(const f of files){mkdirSync(dirname(join(d,f)),{recursive:true});cpSync(join(androidDir,f),join(d,f));}
    const t=readFileSync(join(d,file),'utf8');assert.ok(t.includes(from),file+' lacks '+from);writeFileSync(join(d,file),t.replace(from,to));return checkReleaseConfig(d);};
  ok('release configuration checks catch: debuggable, allowBackup true, a backup include, the whole external storage shared, a password in build.gradle, WebView debugging left on',()=>{
    assert.match(mutated('app/build.gradle','debuggable false','debuggable true').join(),/debuggable false/);
    assert.match(mutated('app/src/productionRelease/AndroidManifest.xml','android:allowBackup="false"','android:allowBackup="true"').join(),/allowBackup/);
    assert.match(mutated('app/src/productionRelease/res/xml/hcc_data_extraction_rules.xml','<exclude domain="sharedpref" path="." />','<include domain="sharedpref" path="." />').join(),/sharedpref|include/);
    assert.match(mutated('app/src/main/res/xml/file_paths.xml','<cache-path name="hcc_exports" path="exports/" />','<external-path name="hcc_exports" path="Documents/HCC/" />').join(),/external or public storage/);
    assert.match(mutated('app/src/main/res/xml/file_paths.xml','path="exports/"','path="."').join(),/whole app cache/);
    // Wave B1 (DEC-073): only the camera's slip-capture folder (Capacitor's getExternalFilesDir(Pictures)) may be shared, exactly as written.
    assert.match(mutated('app/src/main/res/xml/file_paths.xml',SLIP_CAPTURE_PATH,'<external-files-path name="hcc_slip_capture" path="." />').join(),/external or public storage/);
    assert.match(mutated('app/src/main/res/xml/file_paths.xml',SLIP_CAPTURE_PATH,'<external-cache-path name="hcc_slip_capture" path="Pictures/" />').join(),/external or public storage/);
    assert.match(mutated('app/src/main/res/xml/file_paths.xml',SLIP_CAPTURE_PATH,SLIP_CAPTURE_PATH+'\n    <external-path name="x" path="DCIM/" />').join(),/external or public storage/);
    assert.match(mutated('app/src/main/res/xml/file_paths.xml',SLIP_CAPTURE_PATH,SLIP_CAPTURE_PATH+SLIP_CAPTURE_PATH).join(),/more than once/);
    assert.deepEqual(mutated('app/src/main/res/xml/file_paths.xml',SLIP_CAPTURE_PATH,''),[],'without the capture entry the config is still valid (the camera then falls back to the file picker)');
    assert.match(mutated('app/build.gradle','storePassword hccStorePassword',"storePassword 'secret'").join(),/password is written/);
    assert.match(mutated('app/src/main/java/in/hcc/prototype/MainActivity.java','setWebContentsDebuggingEnabled(false)','toString()').join(),/WebView debugging/);
  });
  // ── A10-04: network security config shared by productionDebug and productionRelease ──
  ok('[A10-04] network config as shipped: HTTPS only, trust anchors = bundled hotel CA + system CAs, no user CAs, applied by the productionDebug manifest',()=>{
    assert.deepEqual(checkNetworkConfig(androidDir),[]);
    const nsc='app/src/productionDebug/res/xml/network_security_config.xml';
    // The stricter owner option (trust only the hotel CA, no public CAs) stays valid; a commented-out user anchor is not an anchor.
    assert.deepEqual(mutated(nsc,'<certificates src="system" />',''),[]);
    assert.deepEqual(mutated(nsc,'<certificates src="system" />','<certificates src="system" /><!-- <certificates src="user" /> -->'),[]);
  });
  ok('[A10-04] network config checks catch: user-installed CAs (also in debug-overrides), cleartext, a missing hotel CA, another bundled CA, pin overrides, a manifest without the config',()=>{
    const nsc='app/src/productionDebug/res/xml/network_security_config.xml';
    assert.match(mutated(nsc,'<certificates src="system" />','<certificates src="system" />\n            <certificates src="user" />').join(),/user-installed CAs/);
    assert.match(mutated(nsc,'</network-security-config>','<debug-overrides><trust-anchors><certificates src="user" /></trust-anchors></debug-overrides></network-security-config>').join(),/user-installed CAs/);
    assert.match(mutated(nsc,'cleartextTrafficPermitted="false"','cleartextTrafficPermitted="true"').join(),/refuse cleartext.*must not allow cleartext/);
    assert.match(mutated(nsc,'</network-security-config>','<domain-config cleartextTrafficPermitted="true"><domain>192.168.1.20</domain></domain-config></network-security-config>').join(),/must not allow cleartext/);
    assert.match(mutated(nsc,'<certificates src="@raw/hcc_hotel_ca" />','').join(),/must trust the bundled hotel CA/);
    assert.match(mutated(nsc,'<certificates src="@raw/hcc_hotel_ca" />','<certificates src="@raw/hcc_hotel_ca" /><certificates src="@raw/old_ca" />').join(),/only the bundled hotel CA .*@raw\/old_ca/);
    assert.match(mutated(nsc,'<certificates src="system" />','<certificates src="system" overridePins="true" />').join(),/override pins/);
    assert.match(mutated('app/src/productionDebug/AndroidManifest.xml','android:usesCleartextTraffic="false"','android:usesCleartextTraffic="true"').join(),/productionDebug manifest must keep HTTPS-only/);
  });
  ok('[A10-03] apksigner -v --print-certs parsing (CRLF, Java warnings): verifies, v2 scheme, signer DN and SHA-256; DOES NOT VERIFY is not a pass',()=>{
    assert.deepEqual(parseApksigner(APKSIGNER_THROWAWAY),{verifies:true,schemes:['v2'],signers:[{dn:'CN=APK12 Throwaway',sha256:'3aa410949a17b66e306769999af94e414a5030ffb64ee2a02c5bd5740ba3a2ba'}]});
    assert.equal(parseApksigner(APKSIGNER_DEBUG).signers[0].dn,'C=US, O=Android, CN=Android Debug');
    assert.equal(parseApksigner('DOES NOT VERIFY\nERROR: JAR signer CERT.RSA: JAR signature META-INF/CERT.RSA indicates the APK is signed using APK Signature Scheme v2 but no such signature was found.\n').verifies,false);
    assert.deepEqual(checkSigner(APKSIGNER_HOTEL),{ok:true,problems:[],warnings:[],signers:[{dn:'CN=Hotel Control Centre, O=Hotel Saagar Residency, C=IN',sha256:HOTEL_SIGNER_SHA}],schemes:['v2']});
    assert.match(checkSigner(APKSIGNER_THROWAWAY).problems.join(),/throwaway key/);
    assert.match(checkSigner(APKSIGNER_DEBUG).problems.join(),/Android Debug/);
    assert.match(checkSigner(signerOut('CN=HCC Test Signing','0'.repeat(64))).problems.join(),/throwaway key/);
    assert.match(checkSigner('').problems.join(' | '),/does NOT verify \| apksigner printed no signer/);
    assert.match(checkSigner(APKSIGNER_HOTEL,{expectSignerSha256:'xyz'}).problems.join(),/not a SHA-256 fingerprint/);
    assert.equal(normSha256('16:a6:01:56:A9:8E:F0:AE:FB:BF:93:15:A0:2F:5D:B0:0D:3B:9C:AA:3A:39:F3:02:8E:B9:EC:6E:77:8C:5A:40'),'16A60156A98EF0AEFBBF9315A02F5DB00D3B9CAA3A39F3028EB9EC6E778C5A40');
  });
  ok('[A10-03] the hotel CA resource path comes from aapt\'s resource table (debug res/raw/hcc_hotel_ca.pem, release shortened res/-M.pem, aapt2 "(file)" form)',()=>{
    assert.equal(hotelCaPath(RES_DEBUG),'res/raw/hcc_hotel_ca.pem');
    assert.equal(hotelCaPath(RES_RELEASE.replaceAll('\n','\r\n')),'res/-M.pem');
    assert.equal(hotelCaPath('    resource 0x7f0d0000 raw/hcc_hotel_ca\n      () (file) res/-M.pem type=RAW\n'),'res/-M.pem');
    assert.equal(hotelCaPath(RES_DEBUG.replaceAll('hcc_hotel_ca','other_ca')),undefined);
    assert.equal(hotelCaPath(''),undefined);
  });
  ok('[A10-03] readZipEntry reads stored and deflated APK entries and returns undefined for a missing one; a non-zip is refused',()=>{
    const pemText=Buffer.from('-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n'),html=Buffer.from('<!doctype html>'+'x'.repeat(5000));
    const zip=makeZip([{name:'AndroidManifest.xml',data:Buffer.from([3,0,8,0])},{name:'res/-M.pem',data:pemText},{name:'assets/public/index.html',data:html,deflate:true}]);
    assert.deepEqual(readZipEntry(zip,'res/-M.pem'),pemText);
    assert.deepEqual(readZipEntry(zip,'assets/public/index.html'),html);
    assert.equal(readZipEntry(zip,'res/raw/hcc_hotel_ca.pem'),undefined);
    assert.throws(()=>readZipEntry(Buffer.from('not a zip at all, just text'),'x'),/not a zip/);
  });
  ok('[A10-03] the SDK is found from ANDROID_HOME, android/local.properties or E:\\Android\\Sdk; the newest build-tools version holding the tool wins',()=>{
    const fake=join(dir,'android-props');mkdirSync(fake,{recursive:true});writeFileSync(join(fake,'local.properties'),'## generated\nsdk.dir=D\\:\\\\Sdk2\n');
    assert.deepEqual(sdkCandidates({ANDROID_HOME:'C:\\Sdk1',LOCALAPPDATA:'C:\\Users\\x\\AppData\\Local'},fake,true),['C:\\Sdk1','D:\\Sdk2','E:\\Android\\Sdk',join('C:\\Users\\x\\AppData\\Local','Android','Sdk')]);
    assert.deepEqual(sdkCandidates({},join(dir,'nowhere'),false),[]);
    const sdk=join(dir,'sdk');for(const v of ['9.0.0','35.0.0','36.0.0'])mkdirSync(join(sdk,'build-tools',v),{recursive:true});
    for(const v of ['9.0.0','35.0.0'])writeFileSync(join(sdk,'build-tools',v,'apksigner.bat'),'@echo off');
    writeFileSync(join(sdk,'build-tools','36.0.0','aapt.exe'),'');
    assert.equal(findBuildTool(['apksigner.bat'],[join(dir,'missing'),sdk]),join(sdk,'build-tools','35.0.0','apksigner.bat'));
    assert.equal(findBuildTool(['aapt.exe'],[sdk]),join(sdk,'build-tools','36.0.0','aapt.exe'));
    assert.equal(findBuildTool(['apksigner.bat'],[join(dir,'missing')]),undefined);
  });
  ok('APK dump check: release (in.hcc.server, allowBackup 0, not debuggable) passes; a debug / backup-enabled / prototype APK fails',()=>{
    const good=["package: name='in.hcc.server' versionCode='11' versionName='0.1.0-proto-server'","sdkVersion:'24'","application-label:'HCC Server'",''].join('\n');
    const tree=['E: application (line=10)','  A: android:allowBackup(0x01010280)=(type 0x12)0x0','  A: android:usesCleartextTraffic(0x010104ec)=(type 0x12)0x0',''].join('\n');
    assert.deepEqual(checkApkDump(good,tree),{ok:true,problems:[],pkg:'in.hcc.server'});
    const bad=checkApkDump(good.replace('in.hcc.server','in.hcc.prototype.server')+'application-debuggable\n',
      tree.replace('allowBackup(0x01010280)=(type 0x12)0x0','allowBackup(0x01010280)=(type 0x12)0xffffffff')+'  A: android:debuggable(0x0101000f)=(type 0x12)0xffffffff\n');
    assert.equal(bad.ok,false);assert.equal(bad.problems.length,4,bad.problems.join(' | '));
  });
}finally{rmSync(dir,{recursive:true,force:true});}
{
  const root=mkdtempSync(join(tmpdir(),'hcc-npm-'));
  try{
    ok('[v12fix-b] a checkout without node_modules/@capacitor/preferences is refused with "npm ci"',()=>{
      assert.match(checkCapacitorPlugins(root),/npm ci/);
      mkdirSync(join(root,'node_modules','@capacitor','preferences'),{recursive:true});
      assert.equal(checkCapacitorPlugins(root),'');
    });
  }finally{rmSync(root,{recursive:true,force:true});}
}
console.log(`${n} build-production checks passed`);
