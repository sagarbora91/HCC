# FIT check 20261108-1203

Base http://127.0.0.1:5185/ · passes text 1 (all checks); text 1.3 on tab-land+lap-1366 (checks 1, 2, 5) · roles owner, maint · 129 checks · **3 failing** · FAIL

FAIL kinds (count of findings): font-changed 3

## Text scale 1

Cell = worst over roles; the number is the document height in screens (1.0 = fits).

| Page | tab-land | tab-port | lap-1366 | lap-1280 | desk-1920 |
|---|---|---|---|---|---|
| kds | PASS | PASS | FAIL 1 | FAIL 1 | FAIL 1 |
| maintenance | PASS | PASS | PASS | PASS | PASS |
| maintenance › orders | PASS | PASS | PASS | PASS | PASS |
| maintenance › pm | PASS | PASS | PASS | PASS | PASS |
| maintenance › assets | PASS | PASS | PASS | PASS | PASS |
| maintenance › spares | PASS | PASS | PASS | PASS | PASS |
| maintenance › vendors | PASS | PASS | PASS | PASS | PASS |
| maintenance › reports | PASS | PASS | PASS | PASS | PASS |
| maintenance › pm-calendar | PASS | PASS | PASS | PASS | PASS |
| (chrome) › drawer + user menu | PASS | PASS | PASS | PASS | PASS |

### Per role (checks failing / total)

| Role | tab-land | tab-port | lap-1366 | lap-1280 | desk-1920 |
|---|---|---|---|---|---|
| owner | 0/10 | 0/10 | 1/10 | 1/10 | 1/10 |
| maint | 0/9 | 0/9 | 0/9 | 0/9 | 0/9 |

## Text scale 1.3

Cell = worst over roles; the number is the document height in screens (1.0 = fits).

| Page | tab-land | lap-1366 |
|---|---|---|
| kds | PASS | PASS |
| maintenance | PASS | PASS |
| maintenance › orders | PASS | PASS |
| maintenance › pm | PASS | PASS |
| maintenance › assets | PASS | PASS |
| maintenance › spares | PASS | PASS |
| maintenance › vendors | PASS | PASS |
| maintenance › reports | PASS | PASS |
| maintenance › pm-calendar | PASS | PASS |

### Per role (checks failing / total)

| Role | tab-land | lap-1366 |
|---|---|---|
| owner | 0/9 | 0/9 |
| maint | 0/8 | 0/8 |

## Failures

| Text | Viewport | Role | Page | Why |
|---|---|---|---|---|
| 1 | lap-1366 | owner | kds | font-changed button.btn 15px (new element; main on this page: 10px/11px/13px/14px/16px/9px): «Ready» «Served» |
| 1 | lap-1280 | owner | kds | font-changed button.btn 15px (new element; main on this page: 10px/11px/13px/14px/16px/9px): «Ready» «Served» |
| 1 | desk-1920 | owner | kds | font-changed button.btn 15px (new element; main on this page: 10px/11px/13px/14px/16px/9px): «Ready» «Served» |

Screenshots (Owner + Housekeeper at tab-land and lap-1366, kept out of git): `E:\Projects\HCC\worktrees\f8c-fit\05 Delivery Evidence\F8C\fit\light\screens-20261108-1203`
