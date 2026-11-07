// F12 MEAS · meas-study-a · rpt.study.receivables-bad-debt (catalogue §3.10; selector receivablesBadDebt in domain/analytics/legacy-zero.ts).
import {registerStudy,FY_PARAM} from './shared';
import {receivablesBadDebt} from '../../analytics/legacy-zero';

export const study_receivablesBadDebt=registerStudy({id:'rpt.study.receivables-bad-debt',title:'Receivables and bad debt (legacy + live ageing)',money:true,dateClass:'D-ASOF',params:[{key:'asOf',label:'As of',type:'date',dateClass:'D-ASOF'},{key:'topN',label:'Top N',type:'text',default:'100'}],select:receivablesBadDebt});
