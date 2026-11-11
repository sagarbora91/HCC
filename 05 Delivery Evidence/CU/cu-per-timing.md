# CU-PER timing (re-run in the fix round)

IMPORTANT: the "reconstructed before" rows are NOT measurements of the old code. They were rebuilt on the new code (LoadAsync + Index; the key resolve goes through the new warm token cache, and the duplicates row leaves out the policy, secret, legacy-rollup and summary work the "after" row includes). The old code also paid one HMAC per customer key per lookup, so these rows UNDERSTATE the gain. Debug build, SQL Express, 10 measured runs. The forged-token row is a ProfileAsync with a token that matches no customer, served from the warm cache (cost is the head pass only, no hashing).

### CU-PER N = 1,000 guests
| call | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|
| AddTag + RemoveTag (after) | 23 | 25 | 1.6 |
| key lookup x2 (reconstructed before, warm cache: LoadAsync + Index) | 69 | 87 | 7.9 |
| forged ck_ token ProfileAsync (cache warm, NOT_FOUND) | 10 | 11 | 0.8 |
| DuplicatesAsync (after) | 18 | 19 | 2.2 |
| DuplicatesAsync (reconstructed before: LoadAsync + Index + FindDuplicates only) | 29 | 31 | 4.5 |

### CU-PER N = 10,000 guests
| call | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|
| AddTag + RemoveTag (after) | 74 | 108 | 14.6 |
| key lookup x2 (reconstructed before, warm cache: LoadAsync + Index) | 581 | 654 | 77.4 |
| forged ck_ token ProfileAsync (cache warm, NOT_FOUND) | 33 | 52 | 7.3 |
| DuplicatesAsync (after) | 71 | 83 | 20.8 |
| DuplicatesAsync (reconstructed before: LoadAsync + Index + FindDuplicates only) | 313 | 363 | 45.4 |

### CU-PER N = 15,000 guests
| call | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|
| AddTag + RemoveTag (after) | 103 | 121 | 20.5 |
| key lookup x2 (reconstructed before, warm cache: LoadAsync + Index) | 820 | 962 | 110.2 |
| forged ck_ token ProfileAsync (cache warm, NOT_FOUND) | 51 | 80 | 10.2 |
| DuplicatesAsync (after) | 108 | 120 | 29.3 |
| DuplicatesAsync (reconstructed before: LoadAsync + Index + FindDuplicates only) | 440 | 456 | 64.5 |

