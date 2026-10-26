// A02b-05 · self-service password / PIN change in the demo (production: ChangeOwnCredential, clients/sync/admin.ts). Same rules as the
// server (src/domain/credentials.ts): every signed-in user, own account only; the current secret must be right; exactly one new secret
// of the kind the role signs in with (DEC-062: Owner, Accountant, Tech admin → password; others → 6-digit PIN); PIN / password rules;
// not the current one; no PIN of the last PIN_HISTORY. Only a digest is stored; the audit detail never holds a secret.
import type {Handler} from '../commands';
import {kindOfRole,secretRuleProblem,sameSecretText,demoSecretDigest,demoCredentialOf,PIN_HISTORY,OWN_CREDENTIAL_CHANGE,type CredKind} from '../../../domain/credentials';

const label=(k:CredKind)=>k==='PIN'?'PIN':'PASSWORD';
const ownCredentialChange:Handler=({s,uid,user,cmd,fail,assert})=>{
  const hasPassword=!!cmd.newPassword,hasPin=!!cmd.newPin;
  if(hasPassword===hasPin)fail('VALIDATION','Give either a password or a PIN.');
  const kind:CredKind=hasPin?'PIN':'PASSWORD';
  const secret=String(hasPin?cmd.newPin:cmd.newPassword);const current=String(cmd.current??'');
  assert(current!==secret,sameSecretText(kind));
  const have=demoCredentialOf(s.credentialsX,uid,user.role,s.clock);
  if(kindOfRole(user.role)!==kind)fail('VALIDATION',`Your account does not use a ${label(kind)}.`);
  if(demoSecretDigest(uid,current)!==have.digest)fail('FORBIDDEN','The current password or PIN is wrong.');
  const bad=secretRuleProblem(kind,secret,uid);if(bad)fail('VALIDATION',bad);
  const digest=demoSecretDigest(uid,secret);
  if(kind==='PIN'&&have.history.slice(0,PIN_HISTORY).includes(digest))fail('VALIDATION',`Choose a PIN you have not used for your last ${PIN_HISTORY} PINs.`);
  s.credentialsX={...(s.credentialsX??{}),[uid]:{kind,digest,history:kind==='PIN'?[digest,...have.history].slice(0,PIN_HISTORY):[],changedAt:s.clock}};
  return `Own ${label(kind)} changed.`;
};
export const handlers:Record<string,Handler>={[OWN_CREDENTIAL_CHANGE]:ownCredentialChange};
