// Module 18 · Guest services, complaints, lost & found — fixture extensions (fictional data only). No money is posted.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import type {GSVState} from '../../domain/streams/services';
export function extendBase(s:State):void{
  const d=s.date,y='2026-10-23';
  const x:GSVState={
    requests:[
      {id:'SR-901',booking:'HCC-1041',room:'101',guest:'Ananya Rao',type:'Taxi',detail:'Airport drop at 09:30 · 2 bags',dept:'Front desk',assignee:'reception',priority:'Normal',status:'Done',
        createdAt:d+'T06:40',dueAt:d+'T07:10',startedAt:d+'T06:45',doneAt:d+'T06:58',note:'Taxi confirmed for 09:30 at the porch (simulated booking).',by:'reception',
        log:[{at:d+'T06:40',by:'reception',what:'Logged · assigned to Aarav'},{at:d+'T06:58',by:'reception',what:'Completed'}]},
      {id:'SR-902',booking:'HCC-1042',room:'102',guest:'Vikram Shah',type:'Iron & board',detail:'',dept:'Housekeeping',assignee:'housekeeper2',priority:'Normal',status:'Done',
        createdAt:d+'T07:00',dueAt:d+'T07:15',startedAt:d+'T07:10',doneAt:d+'T07:28',note:'Iron and board placed in the wardrobe.',by:'reception',
        log:[{at:d+'T07:00',by:'reception',what:'Logged · assigned to Kavya'},{at:d+'T07:28',by:'housekeeper2',what:'Completed'}]},
      {id:'SR-903',booking:'HCC-1044',room:'201',guest:'Arjun Kapoor',type:'Extra towels',detail:'2 bath towels',dept:'Housekeeping',priority:'Normal',status:'Open',
        createdAt:d+'T07:30',dueAt:d+'T07:50',by:'reception',log:[{at:d+'T07:30',by:'reception',what:'Logged'}]},
      {id:'SR-904',booking:'HCC-1045',room:'202',guest:'Nisha Desai',type:'Drinking water',detail:'4 bottles',dept:'Housekeeping',assignee:'housekeeper',priority:'Normal',status:'Assigned',
        createdAt:d+'T07:55',dueAt:d+'T08:05',by:'reception',log:[{at:d+'T07:55',by:'reception',what:'Logged · assigned to Ravi'}]},
      {id:'SR-905',booking:'HCC-1047',room:'301',guest:'Tara Iyer',type:'Wake-up call',detail:'Call twice if no answer',dept:'Front desk',priority:'Normal',status:'Open',
        createdAt:d+'T07:45',scheduledFor:'2026-10-25T06:30',dueAt:'2026-10-25T06:35',by:'reception',log:[{at:d+'T07:45',by:'reception',what:'Logged'}]},
      {id:'SR-906',booking:'HCC-1046',room:'206',guest:'Kabir Sen',type:'Minor repair',detail:'Bathroom tap dripping',dept:'Maintenance',assignee:'maint',priority:'Normal',status:'Assigned',
        createdAt:d+'T07:40',dueAt:d+'T08:25',by:'reception',log:[{at:d+'T07:40',by:'reception',what:'Logged · assigned to Joseph'}]},
    ],
    complaints:[
      {id:'CMP-901',booking:'HCC-1045',room:'202',guest:'Nisha Desai',category:'Noise',severity:'Medium',description:'Loud music from the corridor until 00:30; guest could not sleep.',owner:'manager',status:'Open',
        createdAt:d+'T07:20',by:'reception',log:[{at:d+'T07:20',by:'reception',what:'Logged · owner Meera'}]},
      {id:'CMP-902',booking:'HCC-1044',room:'201',guest:'Arjun Kapoor',category:'Room cleanliness',severity:'Low',description:'Dust on the bedside table at check-in.',owner:'reception',status:'Resolved',
        rootCause:'Bedside table missed on the turnover checklist.',recoveryAction:'Room re-cleaned within 20 minutes; supervisor re-briefed the team.',followUpOn:d,
        createdAt:y+'T15:10',resolvedAt:y+'T15:40',by:'reception',log:[{at:y+'T15:10',by:'reception',what:'Logged'},{at:y+'T15:40',by:'reception',what:'Resolved · follow-up '+d}]},
      {id:'CMP-903',guest:'Meghna Kulkarni',category:'Food & beverage',severity:'Low',description:'Breakfast tea served cold (departed guest, phone feedback).',owner:'manager',status:'Closed',
        rootCause:'Tea urn heater switched off early.',recoveryAction:'Apology call; urn timer corrected.',followUpOn:y,followUp:{outcome:'Guest thanked us for calling back.',satisfied:true,at:y+'T18:00',by:'manager'},
        createdAt:'2026-10-21T09:00',resolvedAt:'2026-10-21T12:00',closedAt:y+'T18:00',by:'manager',log:[{at:'2026-10-21T09:00',by:'manager',what:'Logged'},{at:y+'T18:00',by:'manager',what:'Follow-up · satisfied · closed'}]},
    ],
    items:[
      {id:'LF-901',description:'Black phone charger with cable',category:'Electronics',valuable:true,foundWhere:'Room 104 · bedside socket',room:'104',foundAt:'2026-10-22T12:10',foundBy:'housekeeper',
        storage:'Front office safe',status:'Stored',log:[{at:'2026-10-22T12:10',by:'housekeeper',what:'Logged · stored in Front office safe'}]},
      {id:'LF-902',description:'Blue cotton scarf',category:'Clothing',valuable:false,foundWhere:'Lobby sofa',foundAt:'2026-06-30T19:00',foundBy:'reception',
        storage:'Housekeeping store',status:'Stored',log:[{at:'2026-06-30T19:00',by:'reception',what:'Logged · stored in Housekeeping store'}]},
      {id:'LF-903',description:'Single gold-coloured earring',category:'Jewellery',valuable:true,foundWhere:'Restaurant · table 4',foundAt:y+'T21:15',foundBy:'manager',storage:'Front office safe',status:'Claimed',
        claim:{name:'Sunita Pawar',phone:'9800000123',idType:'Driving licence (fictional)',idNote:'Photo matched · ends 4417',matchNote:'Described the stone and the broken clasp correctly.',at:d+'T07:50',by:'reception'},
        log:[{at:y+'T21:15',by:'manager',what:'Logged · stored in Front office safe'},{at:d+'T07:50',by:'reception',what:'Claimed by Sunita Pawar · ID checked'}]},
      {id:'LF-904',description:'Reading glasses in brown case',category:'Personal care',valuable:false,foundWhere:'Room 207 · desk',room:'207',foundAt:'2026-10-19T11:00',foundBy:'housekeeper2',storage:'Back office cupboard',status:'Handed over',
        claim:{name:'Rohan Joshi',phone:'9800000456',idType:'Aadhaar (fictional)',idNote:'Name matched · ends 9031',matchNote:'Described the case colour.',at:'2026-10-20T10:00',by:'reception'},
        handover:{at:'2026-10-20T10:05',by:'reception',to:'Rohan Joshi',note:'Collected in person.'},
        log:[{at:'2026-10-19T11:00',by:'housekeeper2',what:'Logged'},{at:'2026-10-20T10:05',by:'reception',what:'Handed over to Rohan Joshi'}]},
    ],
  };
  s.servicesX=x;
}
export const scenarioPatches:Record<string,(s:State)=>void>={};
export const scenarioInfo:ScenarioInfo[]=[];
