// F12-CAP-PEOPLE · MEAS people captures (MEAS-CONTRACT §2). Every command refuses MEAS_OFF first (payroll module / pack / item off), then checks the HR-001 gate.
// MEAS_TRAINING_SAVE · MEAS_EXIT_SET (HR-001 = hrCanManage: Owner, Manager) · MEAS_PULSE_ADD (any signed-in staff; stores department only, never a user or staff id).
import type {Handler} from '../commands';
import {hrCanManage,hrMeasGuard,hrMeasTrainingSave,hrMeasExitSet,hrMeasPulseAdd,hrStaffById,hrSlice,HR_MEAS_KEYS} from '../../../domain/streams/staff';

const hr=(k:Parameters<Handler>[0])=>{if(!hrCanManage(k.s,k.uid))k.fail('FORBIDDEN','Access denied: HR-001 (staff records) is held by the Owner and Managers.');};
export const handlers:Record<string,Handler>={
  MEAS_TRAINING_SAVE:k=>{
    const {s,cmd,uid}=k;hrMeasGuard(s,HR_MEAS_KEYS.training);hr(k);
    const t=hrMeasTrainingSave(s,uid,{id:cmd.id?String(cmd.id):undefined,staff:String(cmd.staff??''),course:String(cmd.course??''),date:String(cmd.date??''),hours:Number(cmd.hours),
      certificateNo:cmd.certificateNo,validTo:cmd.validTo?String(cmd.validTo):undefined,evidenceRef:cmd.evidenceRef,mandatory:!!cmd.mandatory},k.id('TRN'));
    return `${t.id} · ${hrStaffById(s,t.staff)?.name??t.staff} · ${t.course} · ${t.hours} h${t.validTo?` · valid to ${t.validTo}`:''}`;
  },
  MEAS_EXIT_SET:k=>{
    const {s,cmd}=k;hrMeasGuard(s,HR_MEAS_KEYS.exit);hr(k);
    const r=hrMeasExitSet(s,String(cmd.staff??''),String(cmd.reason??''),String(cmd.exitDate??''));
    return r?`${hrStaffById(s,String(cmd.staff))?.name} · exit reason ${r.reason} · ${r.exitDate}`:`${hrStaffById(s,String(cmd.staff))?.name} · exit reason cleared`;
  },
  // Anonymous: nothing about the signed-in user is read. (The command log row names the actor — see DONE "Integration needed".)
  MEAS_PULSE_ADD:k=>{
    const {s,cmd}=k;hrMeasGuard(s,HR_MEAS_KEYS.pulse);
    const p=hrMeasPulseAdd(s,{dept:String(cmd.dept??''),workload:Number(cmd.workload),respect:Number(cmd.respect),tools:Number(cmd.tools),payFairness:Number(cmd.payFairness),recommend:Number(cmd.recommend)},k.id('PLS'));
    return `Pulse ${p.quarter} · ${hrSlice(s).depts.find(d=>d.id===p.dept)?.name??p.dept} (anonymous)`;
  },
};
