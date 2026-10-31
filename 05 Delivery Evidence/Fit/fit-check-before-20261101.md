# FIT check — BEFORE (main c8f91d45, 1 Nov 2026)

Base http://127.0.0.1:4173/ · text scale 1 · roles owner, reception, housekeeper, accountant · 380 checks · **380 failing** · FAIL

Cell = worst over roles; the number is the document height in screens (1.0 = fits).

| Page | tab-land | tab-port | lap-1366 | lap-1280 | desk-1920 |
|---|---|---|---|---|---|
| home | FAIL 1 | FAIL 1 | FAIL 1.1 | FAIL 1.1 | FAIL 1 |
| rooms | FAIL 2.4 | FAIL 2.4 | FAIL 2.5 | FAIL 2.3 | FAIL 1.6 |
| calendar | FAIL 2.7 | FAIL 1.7 | FAIL 2.8 | FAIL 2.6 | FAIL 1.8 |
| reservations | FAIL 12.9 | FAIL 7.8 | FAIL 11.2 | FAIL 10.2 | FAIL 7.5 |
| arrivals | FAIL 1 | FAIL 1 | FAIL 1 | FAIL 1 | FAIL 1 |
| folios | FAIL 13.4 | FAIL 8.2 | FAIL 11.7 | FAIL 10.7 | FAIL 7.8 |
| cashier | FAIL 2.4 | FAIL 2.1 | FAIL 2.5 | FAIL 2.3 | FAIL 1.6 |
| cashbook | FAIL 2.4 | FAIL 1.6 | FAIL 2.3 | FAIL 2.1 | FAIL 1.5 |
| pos | FAIL 1.7 | FAIL 2.4 | FAIL 1.7 | FAIL 1.8 | FAIL 1 |
| expenses | FAIL 19.2 | FAIL 14 | FAIL 17.2 | FAIL 15.7 | FAIL 11.6 |
| housekeeping | FAIL 12.5 | FAIL 9 | FAIL 11.7 | FAIL 10.7 | FAIL 7.8 |
| maintenance | FAIL 1.7 | FAIL 1.3 | FAIL 1.8 | FAIL 1.6 | FAIL 1.1 |
| handover | FAIL 3.3 | FAIL 3.6 | FAIL 2.5 | FAIL 2.9 | FAIL 1.5 |
| inhouse | FAIL 1.6 | FAIL 1.7 | FAIL 1.6 | FAIL 1.5 | FAIL 1 |
| guest-services | FAIL 1.4 | FAIL 1 | FAIL 1.5 | FAIL 1.4 | FAIL 1 |
| gsv-my-requests | FAIL 1 | FAIL 1 | FAIL 1.1 | FAIL 1 | FAIL 1 |
| messages | FAIL 163.7 | FAIL 102.3 | FAIL 137.8 | FAIL 125.8 | FAIL 93.4 |
| documents | FAIL 3.8 | FAIL 2.4 | FAIL 3.7 | FAIL 3.4 | FAIL 2.1 |
| customers | FAIL 1.9 | FAIL 1.5 | FAIL 2 | FAIL 1.9 | FAIL 1.3 |
| yield | FAIL 1.4 | FAIL 1.1 | FAIL 1.5 | FAIL 1.4 | FAIL 1 |
| groups | FAIL 6.7 | FAIL 4.9 | FAIL 4.2 | FAIL 3.8 | FAIL 2.6 |
| channels | FAIL 2.9 | FAIL 2.1 | FAIL 2.8 | FAIL 2.6 | FAIL 1.8 |
| booking-site | FAIL 13.8 | FAIL 9.1 | FAIL 10.4 | FAIL 11.5 | FAIL 6 |
| inventory | FAIL 3.5 | FAIL 3.5 | FAIL 3.3 | FAIL 3 | FAIL 2.1 |
| staff | FAIL 2.4 | FAIL 2.3 | FAIL 2.3 | FAIL 2.1 | FAIL 1.5 |
| my-hr | FAIL 3 | FAIL 2.6 | FAIL 3 | FAIL 2.8 | FAIL 2 |
| closing | FAIL 11.3 | FAIL 9 | FAIL 10.5 | FAIL 9.7 | FAIL 7.1 |
| reports | FAIL 2.2 | FAIL 1.7 | FAIL 2.4 | FAIL 2.2 | FAIL 1.4 |
| accounting | FAIL 2.6 | FAIL 1.7 | FAIL 2.2 | FAIL 2.1 | FAIL 1.4 |
| assistant | FAIL 2.5 | FAIL 2.2 | FAIL 2.3 | FAIL 2.2 | FAIL 1.5 |
| compliance | FAIL 1.3 | FAIL 1 | FAIL 1.4 | FAIL 1.3 | FAIL 1 |
| access | FAIL 2 | FAIL 1.3 | FAIL 1.9 | FAIL 1.8 | FAIL 1.2 |
| settings | FAIL 1.6 | FAIL 2.9 | FAIL 1.6 | FAIL 1.6 | FAIL 1.2 |
| approvals | FAIL 2.6 | FAIL 1.6 | FAIL 2.6 | FAIL 2.4 | FAIL 1.7 |
| backup | FAIL 1.9 | FAIL 1.4 | FAIL 2 | FAIL 1.9 | FAIL 1.3 |
| roadmap | FAIL 1.7 | FAIL 1.1 | FAIL 1.9 | FAIL 1.7 | FAIL 1.2 |

## Failures

| Viewport | Role | Page | Why |
|---|---|---|---|
| tab-land | owner | home | page-scroll 644/640; not-converted |
| tab-land | owner | rooms | page-scroll 1513/640; not-converted |
| tab-land | owner | calendar | page-scroll 1712/640; not-converted |
| tab-land | owner | reservations | page-scroll 8264/640; not-converted |
| tab-land | owner | arrivals | not-converted |
| tab-land | owner | folios | page-scroll 8571/640; not-converted |
| tab-land | owner | cashier | page-scroll 1554/640; not-converted; hidden-action Submit count & close shi |
| tab-land | owner | cashbook | page-scroll 1563/640; not-converted |
| tab-land | owner | pos | page-scroll 1068/640; not-converted |
| tab-land | owner | expenses | page-scroll 12300/640; not-converted |
| tab-land | owner | housekeeping | page-scroll 7969/640; not-converted |
| tab-land | owner | maintenance | page-scroll 1096/640; not-converted |
| tab-land | owner | handover | page-scroll 2083/640; not-converted |
| tab-land | owner | inhouse | page-scroll 1038/640; not-converted |
| tab-land | owner | guest-services | page-scroll 906/640; not-converted |
| tab-land | owner | gsv-my-requests | page-scroll 660/640; not-converted |
| tab-land | owner | messages | page-scroll 104789/640; not-converted |
| tab-land | owner | documents | page-scroll 2422/640; not-converted |
| tab-land | owner | customers | page-scroll 1226/640; not-converted |
| tab-land | owner | yield | page-scroll 882/640; not-converted |
| tab-land | owner | groups | page-scroll 4298/640; not-converted |
| tab-land | owner | channels | page-scroll 1838/640; not-converted |
| tab-land | owner | booking-site | page-scroll 8851/640; not-converted |
| tab-land | owner | inventory | page-scroll 2267/640; not-converted |
| tab-land | owner | staff | page-scroll 1547/640; not-converted |
| tab-land | owner | my-hr | not-converted |
| tab-land | owner | closing | page-scroll 7146/640; not-converted; hidden-action Request correction |
| tab-land | owner | reports | page-scroll 1408/640; not-converted |
| tab-land | owner | accounting | page-scroll 1636/640; not-converted |
| tab-land | owner | assistant | page-scroll 1577/640; not-converted |
| tab-land | owner | compliance | page-scroll 844/640; not-converted |
| tab-land | owner | access | page-scroll 1272/640; not-converted |
| tab-land | owner | settings | page-scroll 1002/640; not-converted |
| tab-land | owner | approvals | page-scroll 1646/640; not-converted |
| tab-land | owner | backup | page-scroll 1191/640; not-converted |
| tab-land | owner | roadmap | page-scroll 1099/640; not-converted |
| tab-land | reception | home | not-converted |
| tab-land | reception | rooms | page-scroll 1513/640; not-converted |
| tab-land | reception | calendar | page-scroll 1712/640; not-converted |
| tab-land | reception | reservations | page-scroll 8264/640; not-converted |
| tab-land | reception | arrivals | not-converted |
| tab-land | reception | folios | page-scroll 8571/640; not-converted |
| tab-land | reception | cashier | page-scroll 1554/640; not-converted; hidden-action Submit count & close shi |
| tab-land | reception | pos | page-scroll 1068/640; not-converted |
| tab-land | reception | expenses | page-scroll 11271/640; not-converted |
| tab-land | reception | handover | page-scroll 1400/640; not-converted |
| tab-land | reception | inhouse | page-scroll 1038/640; not-converted |
| tab-land | reception | guest-services | page-scroll 906/640; not-converted |
| tab-land | reception | messages | page-scroll 104789/640; not-converted |
| tab-land | reception | documents | page-scroll 2422/640; not-converted |
| tab-land | reception | customers | page-scroll 1226/640; not-converted |
| tab-land | reception | groups | page-scroll 4298/640; not-converted |
| tab-land | reception | channels | page-scroll 1838/640; not-converted |
| tab-land | reception | booking-site | page-scroll 8851/640; not-converted |
| tab-land | reception | my-hr | page-scroll 1881/640; not-converted |
| tab-land | housekeeper | home | not-converted |
| tab-land | housekeeper | housekeeping | page-scroll 1957/640; not-converted |
| tab-land | housekeeper | maintenance | not-converted |
| tab-land | housekeeper | handover | page-scroll 1087/640; not-converted |
| tab-land | housekeeper | gsv-my-requests | page-scroll 660/640; not-converted |
| tab-land | housekeeper | inventory | page-scroll 2230/640; not-converted |
| tab-land | housekeeper | my-hr | page-scroll 1911/640; not-converted |
| tab-land | accountant | home | not-converted |
| tab-land | accountant | folios | page-scroll 8571/640; not-converted |
| tab-land | accountant | cashbook | page-scroll 1563/640; not-converted |
| tab-land | accountant | expenses | page-scroll 12300/640; not-converted |
| tab-land | accountant | yield | page-scroll 862/640; not-converted |
| tab-land | accountant | staff | page-scroll 1547/640; not-converted |
| tab-land | accountant | my-hr | page-scroll 1881/640; not-converted |
| tab-land | accountant | closing | page-scroll 7211/640; not-converted; hidden-action Request correction |
| tab-land | accountant | reports | page-scroll 1438/640; not-converted |
| tab-land | accountant | accounting | page-scroll 1636/640; not-converted |
| tab-land | accountant | assistant | page-scroll 1313/640; not-converted |
| tab-land | accountant | compliance | page-scroll 844/640; not-converted |
| tab-land | accountant | approvals | page-scroll 1646/640; not-converted |
| tab-land | accountant | roadmap | page-scroll 1099/640; not-converted |
| tab-port | owner | home | not-converted |
| tab-port | owner | rooms | page-scroll 2604/1066; not-converted |
| tab-port | owner | calendar | page-scroll 1765/1066; not-converted |
| tab-port | owner | reservations | page-scroll 8355/1066; not-converted |
| tab-port | owner | arrivals | not-converted |
| tab-port | owner | folios | page-scroll 8784/1066; not-converted |
| tab-port | owner | cashier | page-scroll 2189/1066; not-converted; hidden-action Submit count & close shi |
| tab-port | owner | cashbook | page-scroll 1744/1066; not-converted |
| tab-port | owner | pos | page-scroll 2569/1066; not-converted |
| tab-port | owner | expenses | page-scroll 14961/1066; not-converted |
| tab-port | owner | housekeeping | page-scroll 9607/1066; not-converted |
| tab-port | owner | maintenance | page-scroll 1333/1066; not-converted |
| tab-port | owner | handover | page-scroll 3878/1066; not-converted |
| tab-port | owner | inhouse | page-scroll 1802/1066; not-converted |
| tab-port | owner | guest-services | page-scroll 1091/1066; not-converted |
| tab-port | owner | gsv-my-requests | not-converted |
| tab-port | owner | messages | page-scroll 109039/1066; not-converted |
| tab-port | owner | documents | page-scroll 2584/1066; not-converted |
| tab-port | owner | customers | page-scroll 1643/1066; not-converted |
| tab-port | owner | yield | page-scroll 1137/1066; not-converted |
| tab-port | owner | groups | page-scroll 5274/1066; not-converted |
| tab-port | owner | channels | page-scroll 2245/1066; not-converted |
| tab-port | owner | booking-site | page-scroll 9677/1066; not-converted |
| tab-port | owner | inventory | page-scroll 3718/1066; not-converted |
| tab-port | owner | staff | page-scroll 2456/1066; not-converted |
| tab-port | owner | my-hr | not-converted |
| tab-port | owner | closing | page-scroll 9480/1066; not-converted; hidden-action Request correction |
| tab-port | owner | reports | page-scroll 1796/1066; not-converted |
| tab-port | owner | accounting | page-scroll 1843/1066; not-converted |
| tab-port | owner | assistant | page-scroll 2321/1066; not-converted |
| tab-port | owner | compliance | page-scroll 1083/1066; page-sideways 779/711; not-converted |
| tab-port | owner | access | page-scroll 1420/1066; not-converted |
| tab-port | owner | settings | page-scroll 3042/1066; not-converted |
| tab-port | owner | approvals | page-scroll 1669/1066; not-converted |
| tab-port | owner | backup | page-scroll 1498/1066; not-converted |
| tab-port | owner | roadmap | page-scroll 1152/1066; not-converted |
| tab-port | reception | home | not-converted |
| tab-port | reception | rooms | page-scroll 2604/1066; not-converted |
| tab-port | reception | calendar | page-scroll 1765/1066; not-converted |
| tab-port | reception | reservations | page-scroll 8355/1066; not-converted |
| tab-port | reception | arrivals | not-converted |
| tab-port | reception | folios | page-scroll 8784/1066; not-converted |
| tab-port | reception | cashier | page-scroll 2189/1066; not-converted; hidden-action Submit count & close shi |
| tab-port | reception | pos | page-scroll 2569/1066; not-converted |
| tab-port | reception | expenses | page-scroll 13876/1066; not-converted |
| tab-port | reception | handover | page-scroll 1819/1066; not-converted |
| tab-port | reception | inhouse | page-scroll 1802/1066; not-converted |
| tab-port | reception | guest-services | page-scroll 1091/1066; not-converted |
| tab-port | reception | messages | page-scroll 109039/1066; not-converted |
| tab-port | reception | documents | page-scroll 2584/1066; not-converted |
| tab-port | reception | customers | page-scroll 1643/1066; not-converted |
| tab-port | reception | groups | page-scroll 5274/1066; not-converted |
| tab-port | reception | channels | page-scroll 2245/1066; not-converted |
| tab-port | reception | booking-site | page-scroll 9677/1066; not-converted |
| tab-port | reception | my-hr | page-scroll 2756/1066; not-converted |
| tab-port | housekeeper | home | not-converted |
| tab-port | housekeeper | housekeeping | page-scroll 3021/1066; not-converted |
| tab-port | housekeeper | maintenance | not-converted |
| tab-port | housekeeper | handover | page-scroll 1512/1066; not-converted |
| tab-port | housekeeper | gsv-my-requests | not-converted |
| tab-port | housekeeper | inventory | page-scroll 3475/1066; not-converted |
| tab-port | housekeeper | my-hr | page-scroll 2657/1066; not-converted |
| tab-port | accountant | home | not-converted |
| tab-port | accountant | folios | page-scroll 8784/1066; not-converted |
| tab-port | accountant | cashbook | page-scroll 1744/1066; not-converted |
| tab-port | accountant | expenses | page-scroll 14961/1066; not-converted |
| tab-port | accountant | yield | page-scroll 1106/1066; not-converted |
| tab-port | accountant | staff | page-scroll 1784/1066; not-converted |
| tab-port | accountant | my-hr | page-scroll 2756/1066; not-converted |
| tab-port | accountant | closing | page-scroll 9561/1066; not-converted; hidden-action Request correction |
| tab-port | accountant | reports | page-scroll 1826/1066; not-converted |
| tab-port | accountant | accounting | page-scroll 1843/1066; not-converted |
| tab-port | accountant | assistant | page-scroll 2057/1066; not-converted |
| tab-port | accountant | compliance | page-sideways 779/711; not-converted |
| tab-port | accountant | approvals | page-scroll 1669/1066; not-converted |
| tab-port | accountant | roadmap | page-scroll 1152/1066; not-converted |
| lap-1366 | owner | home | page-scroll 722/657; not-converted |
| lap-1366 | owner | rooms | page-scroll 1629/657; not-converted |
| lap-1366 | owner | calendar | page-scroll 1828/657; not-converted |
| lap-1366 | owner | reservations | page-scroll 7353/657; not-converted |
| lap-1366 | owner | arrivals | page-scroll 662/657; not-converted |
| lap-1366 | owner | folios | page-scroll 7675/657; not-converted |
| lap-1366 | owner | cashier | page-scroll 1636/657; not-converted; hidden-action Submit count & close shi |
| lap-1366 | owner | cashbook | page-scroll 1526/657; not-converted |
| lap-1366 | owner | pos | page-scroll 1149/657; not-converted |
| lap-1366 | owner | expenses | page-scroll 11291/657; not-converted |
| lap-1366 | owner | housekeeping | page-scroll 7669/657; not-converted |
| lap-1366 | owner | maintenance | page-scroll 1162/657; not-converted |
| lap-1366 | owner | handover | page-scroll 1633/657; not-converted |
| lap-1366 | owner | inhouse | page-scroll 1052/657; not-converted |
| lap-1366 | owner | guest-services | page-scroll 969/657; not-converted |
| lap-1366 | owner | gsv-my-requests | page-scroll 713/657; not-converted |
| lap-1366 | owner | messages | page-scroll 90552/657; not-converted |
| lap-1366 | owner | documents | page-scroll 2444/657; not-converted |
| lap-1366 | owner | customers | page-scroll 1301/657; not-converted |
| lap-1366 | owner | yield | page-scroll 984/657; not-converted |
| lap-1366 | owner | groups | page-scroll 2728/657; not-converted |
| lap-1366 | owner | channels | page-scroll 1851/657; not-converted |
| lap-1366 | owner | booking-site | page-scroll 6832/657; not-converted |
| lap-1366 | owner | inventory | page-scroll 2144/657; not-converted |
| lap-1366 | owner | staff | page-scroll 1486/657; not-converted |
| lap-1366 | owner | my-hr | not-converted |
| lap-1366 | owner | closing | page-scroll 6843/657; not-converted; hidden-action Request correction |
| lap-1366 | owner | reports | page-scroll 1525/657; not-converted |
| lap-1366 | owner | accounting | page-scroll 1459/657; not-converted |
| lap-1366 | owner | assistant | page-scroll 1541/657; not-converted |
| lap-1366 | owner | compliance | page-scroll 921/657; not-converted |
| lap-1366 | owner | access | page-scroll 1274/657; not-converted |
| lap-1366 | owner | settings | page-scroll 1082/657; not-converted |
| lap-1366 | owner | approvals | page-scroll 1720/657; not-converted |
| lap-1366 | owner | backup | page-scroll 1332/657; not-converted |
| lap-1366 | owner | roadmap | page-scroll 1226/657; not-converted |
| lap-1366 | reception | home | not-converted |
| lap-1366 | reception | rooms | page-scroll 1629/657; not-converted |
| lap-1366 | reception | calendar | page-scroll 1828/657; not-converted |
| lap-1366 | reception | reservations | page-scroll 7353/657; not-converted |
| lap-1366 | reception | arrivals | page-scroll 662/657; not-converted |
| lap-1366 | reception | folios | page-scroll 7675/657; not-converted |
| lap-1366 | reception | cashier | page-scroll 1636/657; not-converted; hidden-action Submit count & close shi |
| lap-1366 | reception | pos | page-scroll 1149/657; not-converted |
| lap-1366 | reception | expenses | page-scroll 11291/657; not-converted |
| lap-1366 | reception | handover | page-scroll 1415/657; not-converted |
| lap-1366 | reception | inhouse | page-scroll 1052/657; not-converted |
| lap-1366 | reception | guest-services | page-scroll 969/657; not-converted |
| lap-1366 | reception | messages | page-scroll 90552/657; not-converted |
| lap-1366 | reception | documents | page-scroll 2444/657; not-converted |
| lap-1366 | reception | customers | page-scroll 1301/657; not-converted |
| lap-1366 | reception | groups | page-scroll 2728/657; not-converted |
| lap-1366 | reception | channels | page-scroll 1851/657; not-converted |
| lap-1366 | reception | booking-site | page-scroll 6832/657; not-converted |
| lap-1366 | reception | my-hr | page-scroll 1947/657; not-converted |
| lap-1366 | housekeeper | home | not-converted |
| lap-1366 | housekeeper | housekeeping | page-scroll 1795/657; not-converted |
| lap-1366 | housekeeper | maintenance | not-converted |
| lap-1366 | housekeeper | handover | page-scroll 1170/657; not-converted |
| lap-1366 | housekeeper | gsv-my-requests | page-scroll 713/657; not-converted |
| lap-1366 | housekeeper | inventory | page-scroll 2144/657; not-converted |
| lap-1366 | housekeeper | my-hr | page-scroll 1977/657; not-converted |
| lap-1366 | accountant | home | not-converted |
| lap-1366 | accountant | folios | page-scroll 7675/657; not-converted |
| lap-1366 | accountant | cashbook | page-scroll 1526/657; not-converted |
| lap-1366 | accountant | expenses | page-scroll 11291/657; not-converted |
| lap-1366 | accountant | yield | page-scroll 984/657; not-converted |
| lap-1366 | accountant | staff | page-scroll 1486/657; not-converted |
| lap-1366 | accountant | my-hr | page-scroll 1947/657; not-converted |
| lap-1366 | accountant | closing | page-scroll 6908/657; not-converted; hidden-action Request correction |
| lap-1366 | accountant | reports | page-scroll 1555/657; not-converted |
| lap-1366 | accountant | accounting | page-scroll 1459/657; not-converted |
| lap-1366 | accountant | assistant | page-scroll 1391/657; not-converted |
| lap-1366 | accountant | compliance | page-scroll 921/657; not-converted |
| lap-1366 | accountant | approvals | page-scroll 1720/657; not-converted |
| lap-1366 | accountant | roadmap | page-scroll 1226/657; not-converted |
| lap-1280 | owner | home | page-scroll 789/720; not-converted |
| lap-1280 | owner | rooms | page-scroll 1647/720; not-converted |
| lap-1280 | owner | calendar | page-scroll 1846/720; not-converted |
| lap-1280 | owner | reservations | page-scroll 7371/720; not-converted |
| lap-1280 | owner | arrivals | not-converted |
| lap-1280 | owner | folios | page-scroll 7693/720; not-converted |
| lap-1280 | owner | cashier | page-scroll 1654/720; not-converted; hidden-action Submit count & close shi |
| lap-1280 | owner | cashbook | page-scroll 1544/720; not-converted |
| lap-1280 | owner | pos | page-scroll 1265/720; not-converted |
| lap-1280 | owner | expenses | page-scroll 11309/720; not-converted |
| lap-1280 | owner | housekeeping | page-scroll 7687/720; not-converted |
| lap-1280 | owner | maintenance | page-scroll 1180/720; not-converted |
| lap-1280 | owner | handover | page-scroll 2078/720; not-converted |
| lap-1280 | owner | inhouse | page-scroll 1070/720; not-converted |
| lap-1280 | owner | guest-services | page-scroll 987/720; not-converted |
| lap-1280 | owner | gsv-my-requests | page-scroll 731/720; not-converted |
| lap-1280 | owner | messages | page-scroll 90570/720; not-converted |
| lap-1280 | owner | documents | page-scroll 2481/720; not-converted |
| lap-1280 | owner | customers | page-scroll 1348/720; not-converted |
| lap-1280 | owner | yield | page-scroll 1002/720; not-converted |
| lap-1280 | owner | groups | page-scroll 2759/720; not-converted |
| lap-1280 | owner | channels | page-scroll 1869/720; not-converted |
| lap-1280 | owner | booking-site | page-scroll 8266/720; not-converted |
| lap-1280 | owner | inventory | page-scroll 2162/720; not-converted |
| lap-1280 | owner | staff | page-scroll 1504/720; not-converted |
| lap-1280 | owner | my-hr | not-converted |
| lap-1280 | owner | closing | page-scroll 6893/720; not-converted; hidden-action Request correction |
| lap-1280 | owner | reports | page-scroll 1558/720; not-converted |
| lap-1280 | owner | accounting | page-scroll 1477/720; not-converted |
| lap-1280 | owner | assistant | page-scroll 1559/720; not-converted |
| lap-1280 | owner | compliance | page-scroll 939/720; not-converted |
| lap-1280 | owner | access | page-scroll 1292/720; not-converted |
| lap-1280 | owner | settings | page-scroll 1117/720; not-converted |
| lap-1280 | owner | approvals | page-scroll 1738/720; not-converted |
| lap-1280 | owner | backup | page-scroll 1350/720; not-converted |
| lap-1280 | owner | roadmap | page-scroll 1244/720; not-converted |
| lap-1280 | reception | home | not-converted |
| lap-1280 | reception | rooms | page-scroll 1647/720; not-converted |
| lap-1280 | reception | calendar | page-scroll 1846/720; not-converted |
| lap-1280 | reception | reservations | page-scroll 7371/720; not-converted |
| lap-1280 | reception | arrivals | not-converted |
| lap-1280 | reception | folios | page-scroll 7693/720; not-converted |
| lap-1280 | reception | cashier | page-scroll 1654/720; not-converted; hidden-action Submit count & close shi |
| lap-1280 | reception | pos | page-scroll 1265/720; not-converted |
| lap-1280 | reception | expenses | page-scroll 11309/720; not-converted |
| lap-1280 | reception | handover | page-scroll 1433/720; not-converted |
| lap-1280 | reception | inhouse | page-scroll 1070/720; not-converted |
| lap-1280 | reception | guest-services | page-scroll 987/720; not-converted |
| lap-1280 | reception | messages | page-scroll 90570/720; not-converted |
| lap-1280 | reception | documents | page-scroll 2481/720; not-converted |
| lap-1280 | reception | customers | page-scroll 1348/720; not-converted |
| lap-1280 | reception | groups | page-scroll 2759/720; not-converted |
| lap-1280 | reception | channels | page-scroll 1869/720; not-converted |
| lap-1280 | reception | booking-site | page-scroll 8266/720; not-converted |
| lap-1280 | reception | my-hr | page-scroll 1965/720; not-converted |
| lap-1280 | housekeeper | home | not-converted |
| lap-1280 | housekeeper | housekeeping | page-scroll 2086/720; not-converted |
| lap-1280 | housekeeper | maintenance | not-converted |
| lap-1280 | housekeeper | handover | page-scroll 1188/720; not-converted |
| lap-1280 | housekeeper | gsv-my-requests | page-scroll 731/720; not-converted |
| lap-1280 | housekeeper | inventory | page-scroll 2162/720; not-converted |
| lap-1280 | housekeeper | my-hr | page-scroll 1995/720; not-converted |
| lap-1280 | accountant | home | not-converted |
| lap-1280 | accountant | folios | page-scroll 7693/720; not-converted |
| lap-1280 | accountant | cashbook | page-scroll 1544/720; not-converted |
| lap-1280 | accountant | expenses | page-scroll 11309/720; not-converted |
| lap-1280 | accountant | yield | page-scroll 1002/720; not-converted |
| lap-1280 | accountant | staff | page-scroll 1504/720; not-converted |
| lap-1280 | accountant | my-hr | page-scroll 1965/720; not-converted |
| lap-1280 | accountant | closing | page-scroll 6958/720; not-converted; hidden-action Request correction |
| lap-1280 | accountant | reports | page-scroll 1588/720; not-converted |
| lap-1280 | accountant | accounting | page-scroll 1477/720; not-converted |
| lap-1280 | accountant | assistant | page-scroll 1409/720; not-converted |
| lap-1280 | accountant | compliance | page-scroll 939/720; not-converted |
| lap-1280 | accountant | approvals | page-scroll 1738/720; not-converted |
| lap-1280 | accountant | roadmap | page-scroll 1244/720; not-converted |
| desk-1920 | owner | home | not-converted |
| desk-1920 | owner | rooms | page-scroll 1556/969; not-converted |
| desk-1920 | owner | calendar | page-scroll 1739/969; not-converted |
| desk-1920 | owner | reservations | page-scroll 7280/969; not-converted |
| desk-1920 | owner | arrivals | not-converted |
| desk-1920 | owner | folios | page-scroll 7602/969; not-converted |
| desk-1920 | owner | cashier | page-scroll 1563/969; not-converted |
| desk-1920 | owner | cashbook | page-scroll 1453/969; not-converted |
| desk-1920 | owner | pos | page-scroll 1013/969; not-converted |
| desk-1920 | owner | expenses | page-scroll 11218/969; not-converted |
| desk-1920 | owner | housekeeping | page-scroll 7596/969; not-converted |
| desk-1920 | owner | maintenance | page-scroll 1089/969; not-converted |
| desk-1920 | owner | handover | page-scroll 1412/969; not-converted |
| desk-1920 | owner | inhouse | page-scroll 979/969; not-converted |
| desk-1920 | owner | guest-services | not-converted |
| desk-1920 | owner | gsv-my-requests | not-converted |
| desk-1920 | owner | messages | page-scroll 90479/969; not-converted |
| desk-1920 | owner | documents | page-scroll 2080/969; not-converted |
| desk-1920 | owner | customers | page-scroll 1228/969; not-converted |
| desk-1920 | owner | yield | not-converted |
| desk-1920 | owner | groups | page-scroll 2527/969; not-converted |
| desk-1920 | owner | channels | page-scroll 1778/969; not-converted |
| desk-1920 | owner | booking-site | page-scroll 5849/969; not-converted |
| desk-1920 | owner | inventory | page-scroll 2071/969; not-converted |
| desk-1920 | owner | staff | page-scroll 1413/969; not-converted |
| desk-1920 | owner | my-hr | not-converted |
| desk-1920 | owner | closing | page-scroll 6787/969; not-converted; hidden-action Request correction |
| desk-1920 | owner | reports | page-scroll 1347/969; not-converted |
| desk-1920 | owner | accounting | page-scroll 1386/969; not-converted |
| desk-1920 | owner | assistant | page-scroll 1468/969; not-converted |
| desk-1920 | owner | compliance | not-converted |
| desk-1920 | owner | access | page-scroll 1201/969; not-converted |
| desk-1920 | owner | settings | page-scroll 1164/969; not-converted |
| desk-1920 | owner | approvals | page-scroll 1647/969; not-converted |
| desk-1920 | owner | backup | page-scroll 1243/969; not-converted |
| desk-1920 | owner | roadmap | page-scroll 1153/969; not-converted |
| desk-1920 | reception | home | not-converted |
| desk-1920 | reception | rooms | page-scroll 1556/969; not-converted |
| desk-1920 | reception | calendar | page-scroll 1739/969; not-converted |
| desk-1920 | reception | reservations | page-scroll 7280/969; not-converted |
| desk-1920 | reception | arrivals | not-converted |
| desk-1920 | reception | folios | page-scroll 7602/969; not-converted |
| desk-1920 | reception | cashier | page-scroll 1563/969; not-converted |
| desk-1920 | reception | pos | page-scroll 1013/969; not-converted |
| desk-1920 | reception | expenses | page-scroll 11218/969; not-converted |
| desk-1920 | reception | handover | page-scroll 1342/969; not-converted |
| desk-1920 | reception | inhouse | page-scroll 979/969; not-converted |
| desk-1920 | reception | guest-services | not-converted |
| desk-1920 | reception | messages | page-scroll 90479/969; not-converted |
| desk-1920 | reception | documents | page-scroll 2080/969; not-converted |
| desk-1920 | reception | customers | page-scroll 1228/969; not-converted |
| desk-1920 | reception | groups | page-scroll 2527/969; not-converted |
| desk-1920 | reception | channels | page-scroll 1778/969; not-converted |
| desk-1920 | reception | booking-site | page-scroll 5849/969; not-converted |
| desk-1920 | reception | my-hr | page-scroll 1874/969; not-converted |
| desk-1920 | housekeeper | home | not-converted |
| desk-1920 | housekeeper | housekeeping | page-scroll 1722/969; not-converted |
| desk-1920 | housekeeper | maintenance | not-converted |
| desk-1920 | housekeeper | handover | page-scroll 1097/969; not-converted |
| desk-1920 | housekeeper | gsv-my-requests | not-converted |
| desk-1920 | housekeeper | inventory | page-scroll 2071/969; not-converted |
| desk-1920 | housekeeper | my-hr | page-scroll 1904/969; not-converted |
| desk-1920 | accountant | home | not-converted |
| desk-1920 | accountant | folios | page-scroll 7602/969; not-converted |
| desk-1920 | accountant | cashbook | page-scroll 1453/969; not-converted |
| desk-1920 | accountant | expenses | page-scroll 11218/969; not-converted |
| desk-1920 | accountant | yield | not-converted |
| desk-1920 | accountant | staff | page-scroll 1413/969; not-converted |
| desk-1920 | accountant | my-hr | page-scroll 1874/969; not-converted |
| desk-1920 | accountant | closing | page-scroll 6852/969; not-converted; hidden-action Request correction |
| desk-1920 | accountant | reports | page-scroll 1377/969; not-converted |
| desk-1920 | accountant | accounting | page-scroll 1386/969; not-converted |
| desk-1920 | accountant | assistant | page-scroll 1318/969; not-converted |
| desk-1920 | accountant | compliance | not-converted |
| desk-1920 | accountant | approvals | page-scroll 1647/969; not-converted |
| desk-1920 | accountant | roadmap | page-scroll 1153/969; not-converted |
