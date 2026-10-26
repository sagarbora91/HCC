// Payroll files from the server (W4 gap G2) — production build only (reached from `process.env.HCC_BUILD==='production'` branches in
// features/staff/HrDesk.tsx). The prototype builds the register, PF / ESIC / PT statement and bank file in the browser from its own
// demo data; in production the loaded staff master holds MASKED account numbers (••••1234), so a browser-built bank file would be
// useless (and wrong). The server builds every file from its own run snapshot instead (Payroll module, PayrollEndpoints.cs):
//   register   GET /payroll/runs/{month}/register.csv            PERM-HR-006, audited (HR_REGISTER_DOWNLOADED)
//   statutory  GET /payroll/runs/{month}/statutory.csv           PERM-HR-006, audited (HR_STATUTORY_DOWNLOADED)
//   bank       GET /payroll/run/{runId}/bank-files/latest        PERM-HR-006, locked run, after IssueBankFile (the latest file of the
//                                                                current lock, account numbers frozen at lock), audited (HR_BANK_FILE_DOWNLOADED)
// The text is saved through the native hook (saveCsv: Documents/HCC + share sheet in the APK, a download in a browser).
import type {HttpTransport} from '../http';
import {serverId} from './map';

export type PayrollFileKind='register'|'bank'|'statutory';
/** File names the server uses (Content-Disposition); used when the response carries no header (e.g. CapacitorHttp). */
export function payrollFileName(kind:PayrollFileKind,month:string,issueNo?:number){
  return kind==='register'?`payroll-register-${month}.csv`:kind==='statutory'?`pf-esic-pt-${month}.csv`:`bank-transfer-${month}${issueNo?'-'+issueNo:''}.csv`;
}
/** The server path of a payroll file. `runId` = the prototype run id (= String(server run id)). */
export function payrollFilePath(kind:PayrollFileKind,run:{id:string;month:string}){
  if(!/^\d{4}-\d{2}$/.test(run.month))throw new Error('Payroll month must be YYYY-MM.');
  return kind==='register'?'/payroll/runs/'+run.month+'/register.csv':kind==='statutory'?'/payroll/runs/'+run.month+'/statutory.csv'
    :'/payroll/run/'+serverId(run.id,'Payroll run')+'/bank-files/latest';
}
/** Downloads one payroll file from the server and hands it to `save` (the native saveCsv hook). Returns the saved file name.
 *  Errors are the transport's typed HccClientError (FORBIDDEN, NOT_FOUND, INVALID_TRANSITION, UNAVAILABLE …). */
export async function downloadServerPayrollFile(t:Pick<HttpTransport,'getText'>,kind:PayrollFileKind,run:{id:string;month:string},
  save:(name:string,csv:string)=>Promise<unknown>,issueNo?:number):Promise<string>{
  const f=await t.getText(payrollFilePath(kind,run));
  const name=f.fileName&&/^[\w.-]+\.csv$/.test(f.fileName)?f.fileName:payrollFileName(kind,run.month,issueNo);
  await save(name,f.text);
  return name;
}
