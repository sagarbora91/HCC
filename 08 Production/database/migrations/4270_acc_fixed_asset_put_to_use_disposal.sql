-- HCC migration 4270 · ACC-3 acc3-carry C1 / C2 (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7; rulebook F2, F4). The fixed-asset register
-- gains who set the put-to-use date (depreciation counts from it: ≥ 180 days in the year full rate, else half), the disposal's tender and the
-- rulebook F4 flag ("short-term capital gain / loss — CA" when the block goes negative or empty), and who disposed it. A sale is a manual
-- Receipt voucher Dr tender · Cr block (disposed_voucher = its id); a scrap with nothing received posts nothing. C2: the register cost is net
-- of the posted returns against the purchase (computed by the read; cost_paise stays the purchase cost).
ALTER TABLE acc.fixed_asset ADD
  put_to_use_by INT NULL,
  put_to_use_utc DATETIME2(3) NULL,
  disposal_method NVARCHAR(60) NULL,
  disposal_flag NVARCHAR(300) NULL,
  disposal_reason NVARCHAR(200) NULL,
  disposed_by INT NULL,
  disposed_utc DATETIME2(3) NULL;
GO
