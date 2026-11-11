### CU-PER N = 1,000 guests
| call | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|
| AddTag + RemoveTag (after) | 25 | 25 | 1.5 |
| key lookup x2 (before: LoadAsync + Index) | 67 | 69 | 7.8 |
| DuplicatesAsync (after) | 18 | 20 | 2.2 |
| DuplicatesAsync (before: LoadAsync + Index + FindDuplicates) | 28 | 32 | 4.5 |

### CU-PER N = 10,000 guests
| call | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|
| AddTag + RemoveTag (after) | 81 | 92 | 14.3 |
| key lookup x2 (before: LoadAsync + Index) | 580 | 638 | 77.0 |
| DuplicatesAsync (after) | 66 | 86 | 20.8 |
| DuplicatesAsync (before: LoadAsync + Index + FindDuplicates) | 308 | 344 | 45.4 |

### CU-PER N = 15,000 guests
| call | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|
| AddTag + RemoveTag (after) | 102 | 119 | 19.8 |
| key lookup x2 (before: LoadAsync + Index) | 832 | 930 | 109.5 |
| DuplicatesAsync (after) | 106 | 157 | 29.3 |
| DuplicatesAsync (before: LoadAsync + Index + FindDuplicates) | 433 | 503 | 64.5 |

