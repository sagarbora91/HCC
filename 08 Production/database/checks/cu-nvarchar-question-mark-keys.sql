-- CU-NVARCHAR check: customer keys that contain a question mark.
-- CustomerRules.NormName never writes '?' (punctuation becomes a space), so a '?' in a customer key is a collapse marker left by the old
-- VARCHAR columns (a Devanagari name stored before migrations 4930-4932). Expected result on a clean database: 0 rows.
-- Such rows cannot be repaired automatically (the original text was lost at write time); list them, then ask the front desk to re-tag.
-- One read-only SELECT (no trailing semicolon needed); run it before and after the migrations on a copy of the live database.
SELECT 'crm.customer_merge.source_key' AS key_column, source_key AS customer_key, COUNT(*) AS row_count FROM crm.customer_merge WHERE source_key LIKE N'%?%' GROUP BY source_key
UNION ALL
SELECT 'crm.customer_merge.target_key' AS key_column, target_key AS customer_key, COUNT(*) AS row_count FROM crm.customer_merge WHERE target_key LIKE N'%?%' GROUP BY target_key
UNION ALL
SELECT 'crm.customer_alias.alias_key' AS key_column, alias_key AS customer_key, COUNT(*) AS row_count FROM crm.customer_alias WHERE alias_key LIKE N'%?%' GROUP BY alias_key
UNION ALL
SELECT 'crm.customer_alias.target_key' AS key_column, target_key AS customer_key, COUNT(*) AS row_count FROM crm.customer_alias WHERE target_key LIKE N'%?%' GROUP BY target_key
UNION ALL
SELECT 'crm.customer_tag.customer_key' AS key_column, customer_key AS customer_key, COUNT(*) AS row_count FROM crm.customer_tag WHERE customer_key LIKE N'%?%' GROUP BY customer_key
UNION ALL
SELECT 'crm.customer_note.customer_key' AS key_column, customer_key AS customer_key, COUNT(*) AS row_count FROM crm.customer_note WHERE customer_key LIKE N'%?%' GROUP BY customer_key
UNION ALL
SELECT 'crm.customer_pref.customer_key' AS key_column, customer_key AS customer_key, COUNT(*) AS row_count FROM crm.customer_pref WHERE customer_key LIKE N'%?%' GROUP BY customer_key
UNION ALL
SELECT 'lgc.guest_map.customer_key' AS key_column, customer_key AS customer_key, COUNT(*) AS row_count FROM lgc.guest_map WHERE customer_key LIKE N'%?%' GROUP BY customer_key
UNION ALL
SELECT 'lgc.stay.customer_key' AS key_column, customer_key AS customer_key, COUNT(*) AS row_count FROM lgc.stay WHERE customer_key LIKE N'%?%' GROUP BY customer_key
UNION ALL
SELECT 'lgc.bill.customer_key' AS key_column, customer_key AS customer_key, COUNT(*) AS row_count FROM lgc.bill WHERE customer_key LIKE N'%?%' GROUP BY customer_key
UNION ALL
SELECT 'lgc.receipt.customer_key' AS key_column, customer_key AS customer_key, COUNT(*) AS row_count FROM lgc.receipt WHERE customer_key LIKE N'%?%' GROUP BY customer_key;
