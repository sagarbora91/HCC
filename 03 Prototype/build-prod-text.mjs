// Production-only wording clean-up (W4 N3 / E2E2 N7). The prototype keeps its demo wording; the production bundle must not show
// "demo / simulated / fictional / demonstration" text to a real hotel. esbuild runs these rules over the SOURCE TEXT of screen files
// (never src/clients/sync, fixtures, mock services) while building the production flavour only, so the prototype build is byte-identical.
// Rules are phrase based (spaces/punctuation), never identifiers. tests/prod-wording.mjs builds the bundle and checks the result.
import {readFile} from 'node:fs/promises';
import {sep} from 'node:path';

const Q='[^.<>"\'\x60{}\n;=()/*]'; // one text run: no quotes, tags, braces, newlines or code punctuation
export const RULES=[
  // parenthesised flavour tags: "Aadhaar (demo)", "Retry queued (simulated)", "Generated (simulated clock)"
  [/\s*\((?:simulated clock|simulated|Simulated|demo|fictional)\)/g,''],
  // headers and watermarks
  [/Hotel Control Centre [\u2014-] DEMONSTRATION \u00B7 fictional data \u00B7 /g,'Hotel Control Centre \u2014 '],
  [/Hotel Control Centre [\u2014-] DEMONSTRATION \u00B7 fictional data/g,'Hotel Control Centre'],
  [/Hotel Control Centre \u00B7 Fictional property/g,'Hotel Control Centre'],
  [/Fictional property \u00B7 GST illustrative/g,'GST illustrative'],
  [/Guest registration card \u00B7 Fictional property/g,'Guest registration card'],
  [/DEMONSTRATION \u2014 FICTIONAL DATA \u00B7 NOT A TAX DOCUMENT/g,'NOT A TAX DOCUMENT'],
  [/DEMONSTRATION [\u2014-] FICTIONAL \u00B7 BOOKING CONFIRMATION \u00B7 NOT A TAX INVOICE/g,'BOOKING CONFIRMATION \u00B7 NOT A TAX INVOICE'],
  [/DEMONSTRATION [\u2014-] FICTIONAL \u00B7 NOT A STATUTORY REGISTRATION RECORD/g,'NOT A STATUTORY REGISTRATION RECORD'],
  [/DEMONSTRATION [\u2014-] FICTIONAL/g,''],
  [/DEMONSTRATION PAYSLIP/g,'PAYSLIP'],
  [/FICTIONAL SAMPLE \u00B7 NOT A REAL DOCUMENT/g,'NOT A TAX DOCUMENT'],
  [/SAMPLE \u00B7 NOT A TAX INVOICE/g,'NOT A TAX INVOICE'],
  [/Demonstration [\u2014-] not a tax invoice(?: \u00B7 fictional records only)?/g,'Not a tax invoice'],
  [/Demonstration document \u00B7 fictional data \u00B7 GST illustrative [\u2014-] not a tax invoice\./g,'GST illustrative \u2014 not a tax invoice.'],
  [/ \u00B7 Fictional demonstration data/g,''],
  [/ \u00B7 fictional data/g,''],
  [/ \u00B7 fictional/g,''],
  [/ \u00B7 SIMULATED/g,''],
  // clocks
  [/Simulated hotel clock \(Asia\/Kolkata\)/g,'Hotel clock (Asia/Kolkata)'],
  [/Simulated business date/g,'Business date'],
  [/Demo \u00B7 simulated clock/g,'Hotel clock'],
  [/Generated at simulated clock/g,'Generated at'],
  [/Simulated clock \+/g,'Clock +'],
  [/[Ss]imulated hotel clock/g,'hotel clock'],
  [/Simulated clock/g,'Hotel clock'],
  [/simulated clock/g,'hotel clock'],
  [/simulated hours/g,'hours'],
  [/simulated time/g,'time'],
  [/actor and simulated time/g,'actor and time'],
  // payment references
  [/Demonstration transaction reference/g,'Transaction reference'],
  [/Demo transaction reference, e\.g\. DEMO-UPI-0002/g,'Transaction reference'],
  [/Demo transaction reference/g,'Transaction reference'],
  [/an? demonstration transaction reference/g,'the transaction reference'],
  [/'Demo cash'/g,"'Cash'"],
  [/e\.g\. DEMO-UPI-1234/g,'UPI reference'],
  [/e\.g\. DEMO-CARD-5678/g,'card approval code'],
  [/ This is a fictional demonstration receipt; no money moves\./g,''],
  [/ Fictional demonstration receipt; no money moves\./g,''],
  [/a sample number from DEMO-CN\/2026-27\/ is issued/g,'a credit note number is issued'],
  // placeholders and entry hints
  [/e\.g\. Demo (?:Guest|Travel Hub)/g,'e.g. Guest name'],
  [/e\.g\. Demo Industries \(fictional\)/g,'e.g. Company name'],
  [/e\.g\. Manjra Textiles \(fictional\)/g,'e.g. Supplier name'],
  [/Fictional vendor name/g,'Vendor name'],
  [/Fictional email \(optional\)/g,'Email (optional)'],
  [/, optional, fictional\)/g,', optional)'],
  [/ \(optional, fictional\)/g,' (optional)'],
  [/\(fictional(?: number| format)?\)/g,''],
  [/ \(fictional values only in the prototype\)/g,''],
  [/ in this demo\b/g,''],
  [/ in this prototype\b/g,''],
  [/Maximum 30 nights in this demo\./g,'Maximum 30 nights.'],
  // demo users / toolbar wording
  [/Switch to an enabled user with the demo controls to continue\./g,'Sign in as an enabled user to continue.'],
  [/Switch to another authorized demo user\./g,'Ask another authorised user.'],
  [/Switch user in the demo toolbar\./g,'Ask another authorised user.'],
  [/Your demo user is not linked to a staff record\./g,'Your user is not linked to a staff record.'],
  [/Preview as demo user/g,'Preview as user'],
  [/Self-service for staff with a demo login/g,'Self-service for staff with a login'],
  [/linked demo login/g,'linked login'],
  [/may link to a demo login/g,'may link to a login'],
  [/signed-in demo user/g,'signed-in user'],
  [/Create a demo user/g,'Create a user'],
  [/Demo login/g,'Login'],
  [/Demo &amp; display/g,'Display'],
  [/Demo tools are not staff features\./g,''],
  // simulated integrations (shown only where the production screen still has the prototype copy)
  [/(?<![A-Za-z])Simulated[:.]? (?:— )?/g,''],
  [/ simulated /g,' '],
  // sentences that are only prototype disclaimers
  [new RegExp('\\s?(?:All|Every)'+Q+'*\\bfictional\\b'+Q+'*\\.','g'),''],
  [new RegExp('\\s?'+Q+'*\\b[Ff]ictional\\b'+Q+'*\\.','g'),''],
  [/\bfictional\b/g,''],
  [/Demonstrat(?:ion|e)s? /g,''],
  [/Prototype boundaries/g,'Scope'],
  [/Prototype screen: /g,''],
  [/Prototype disposition/g,'Disposition'],
  [/Built in prototype \(simulated integrations where external\)/g,'Built'],
  [/ \(simulated [A-Za-z ]+\)/g,''],
  [/ · simulated\b/g,''],
  [/ Printed from the HCC interactive prototype\./g,''],
  [/Printed from Hotel Control Centre prototype/g,'Printed from Hotel Control Centre'],
  [/Minimal demo profile/g,'Minimal guest profile'],
  [/\bdemonstration\b ?/g,''],
  [/\bdemo (?:user|data|profile)\b/g,'data'],
  [/\b(?:this|the) (?:demo|prototype)\b/g,'the system'],
  [/Demo (?:CSV downloaded|Excel workbook saved)/g,'Saved'],
  [/HCC-Demo-/g,'HCC-'],
  [/e\.g\. Demo Industries/g,'e.g. Company name'],
  [/ · FICTIONAL DATA · /g,' · '],
  [/· DEMONSTRATION DOCUMENT/g,''],
];

/** esbuild plugin: applies RULES to screen source files (not the sync layer, fixtures or mock services). */
export function prodTextPlugin(srcDir){
  const skip=['clients'+sep,'fixtures'+sep,'services'+sep,'tests'+sep];
  return {name:'prod-text',setup(b){
    b.onLoad({filter:/\.(tsx?|mjs)$/},async a=>{
      if(!a.path.startsWith(srcDir))return undefined;
      const rel=a.path.slice(srcDir.length+1);
      if(skip.some(s=>rel.startsWith(s)))return undefined;
      let text=await readFile(a.path,'utf8');
      for(const [re,to] of RULES)text=text.replace(re,to);
      return {contents:text,loader:a.path.endsWith('x')?'tsx':'ts'};
    });
  }};
}
