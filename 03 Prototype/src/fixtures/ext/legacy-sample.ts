// LG-UI · Legacy ("Lodge" desktop app) stay history — six FICTIONAL stays for two seed guests, served ONLY by the customers legacy read
// (the prototype's stand-in for `GET /customers/profile` → `LegacyStays`). No State slice, no persistence, no SCHEMA bump.
// Keys are the Customer 360 keys (last 10 digits of the mobile number): Ananya Rao 98200 01041, Vikram Shah 98200 01042.
// Money = integer paise (net amount of the old bill). Bill numbers and booking ids are invented.
import type {LegacyStay} from '../../domain/streams/customers';

export const LEGACY_SAMPLE:Record<string,LegacyStay[]>={
  '9820001041':[
    {fy:'2024-25',bookingId:30112,billNo:2210,arrival:'2024-11-02',departure:'2024-11-05',nights:3,room:'105',roomCount:1,status:'Departed',net:690000},
    {fy:'2025-26',bookingId:41877,billNo:3098,arrival:'2025-06-14',departure:'2025-06-16',nights:2,room:'207',roomCount:1,status:'Departed',net:512000},
    {fy:'2026-27',bookingId:52031,billNo:1204,arrival:'2026-05-03',departure:'2026-05-04',nights:1,room:'105',roomCount:1,status:'Departed',net:236000},
  ],
  '9820001042':[
    {fy:'2023-24',bookingId:18455,billNo:1876,arrival:'2023-12-27',departure:'2023-12-30',nights:3,room:'301, 302',roomCount:2,status:'Departed',net:1480000},
    {fy:'2025-26',bookingId:44190,billNo:3341,arrival:'2025-10-09',departure:'2025-10-11',nights:2,room:'204',roomCount:1,status:'Departed',net:498000},
    {fy:'2025-26',bookingId:45012,arrival:'2026-02-20',departure:'2026-02-22',nights:2,room:'204',roomCount:1,status:'Cancelled'},
  ],
};
