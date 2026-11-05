// F12-CAP-PEOPLE · people captures in the production sync layer (commands only; the measX slice has no single owner, so no loader here).
//   server  POST /commands/SaveMeasTraining | SetMeasExit | AddMeasPulse  (tables meas.training, meas.staff_exit, meas.pulse; migration 4825-4827)
//   The pulse mapper sends NO user or staff id; the server endpoint must not read the caller's identity into the row (PeopleRules.PulseHasNoIdentity).
import type {SyncArea} from './types';
import {serverId} from './map';

export const measPeople:SyncArea={
  id:'meas-people',
  loaders:[],
  commands:[
    {type:'MEAS_TRAINING_SAVE',reload:[],async send(c){const k=c.cmd;
      await c.api.command('SaveMeasTraining',{trainingId:k.id?String(k.id):undefined,staffId:serverId(k.staff,'Staff'),course:String(k.course??''),date:String(k.date??''),hours:Number(k.hours),
        certificateNo:k.certificateNo?String(k.certificateNo):undefined,validTo:k.validTo?String(k.validTo):undefined,evidenceRef:k.evidenceRef?String(k.evidenceRef):undefined,mandatory:!!k.mandatory});}},
    {type:'MEAS_EXIT_SET',reload:[],async send(c){const k=c.cmd;
      await c.api.command('SetMeasExit',{staffId:serverId(k.staff,'Staff'),reason:String(k.reason??''),exitDate:k.exitDate?String(k.exitDate):undefined});}},
    {type:'MEAS_PULSE_ADD',reload:[],async send(c){const k=c.cmd;
      await c.api.command('AddMeasPulse',{departmentId:String(k.dept??''),workload:Number(k.workload),respect:Number(k.respect),tools:Number(k.tools),payFairness:Number(k.payFairness),recommend:Number(k.recommend)});}},
  ],
};
