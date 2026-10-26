// Checks for scripts/build-production.mjs (W4-12): the hotel CA file is a public X.509 certificate, never a private key.
// The valid-certificate case needs openssl on PATH (Git for Windows ships it) and is skipped otherwise.
import assert from 'node:assert/strict';
import {mkdtempSync,writeFileSync,readFileSync,rmSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {spawnSync} from 'node:child_process';
import {readHotelCa} from './build-production.mjs';

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
}finally{rmSync(dir,{recursive:true,force:true});}
console.log(`${n} build-production checks passed`);
