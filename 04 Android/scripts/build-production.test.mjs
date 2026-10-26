// Checks for scripts/build-production.mjs (W4-12): the hotel CA file is a public X.509 certificate, never a private key.
// The valid-certificate case needs openssl on PATH (Git for Windows ships it) and is skipped otherwise.
import assert from 'node:assert/strict';
import {mkdtempSync,writeFileSync,readFileSync,rmSync,existsSync,mkdirSync,cpSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
import {readHotelCa,checkReleaseConfig,checkApkDump} from './build-production.mjs';
const androidDir=join(dirname(fileURLToPath(import.meta.url)),'..','android');

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
  }else console.log('SKIP valid-certificate case (openssl not available)');

  // ── Security review F-H02 / F-L10: production release configuration ──
  ok('release configuration as shipped: not debuggable, hccRelease signing from env / keystore.properties, no backup, narrow FileProvider, WebView debugging off',()=>{
    assert.deepEqual(checkReleaseConfig(androidDir),[]);
  });
  const files=['app/build.gradle','app/src/productionRelease/AndroidManifest.xml','app/src/productionRelease/res/xml/hcc_data_extraction_rules.xml',
    'app/src/productionRelease/res/xml/hcc_full_backup_rules.xml','app/src/main/res/xml/file_paths.xml','app/src/main/java/in/hcc/prototype/MainActivity.java'];
  const mutated=(file,from,to)=>{const d=join(dir,'android-'+Math.random().toString(36).slice(2));for(const f of files){mkdirSync(dirname(join(d,f)),{recursive:true});cpSync(join(androidDir,f),join(d,f));}
    const t=readFileSync(join(d,file),'utf8');assert.ok(t.includes(from),file+' lacks '+from);writeFileSync(join(d,file),t.replace(from,to));return checkReleaseConfig(d);};
  ok('release configuration checks catch: debuggable, allowBackup true, a backup include, the whole external storage shared, a password in build.gradle, WebView debugging left on',()=>{
    assert.match(mutated('app/build.gradle','debuggable false','debuggable true').join(),/debuggable false/);
    assert.match(mutated('app/src/productionRelease/AndroidManifest.xml','android:allowBackup="false"','android:allowBackup="true"').join(),/allowBackup/);
    assert.match(mutated('app/src/productionRelease/res/xml/hcc_data_extraction_rules.xml','<exclude domain="sharedpref" path="." />','<include domain="sharedpref" path="." />').join(),/sharedpref|include/);
    assert.match(mutated('app/src/main/res/xml/file_paths.xml','path="Documents/HCC/"','path="."').join(),/whole external storage/);
    assert.match(mutated('app/build.gradle','storePassword hccStorePassword',"storePassword 'secret'").join(),/password is written/);
    assert.match(mutated('app/src/main/java/in/hcc/prototype/MainActivity.java','setWebContentsDebuggingEnabled(false)','toString()').join(),/WebView debugging/);
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
console.log(`${n} build-production checks passed`);
