# V10-1 Phone invoice and A4 print show one GST column, no CGST / SGST split

Severity S3 · APK v10 (versionCode 10), Android phone shell, AVD codex_api35_google (EMULATED), scenario S0.

Steps: S0, run the walkthrough to 26 Oct, settle Room 105 (balance 3,265, deposit returned), Aarav > Folios > Rahul Verma > More > Checkout > Confirm checkout; the invoice opens. Tap Print / share.

Expected (DemoModals step 7 and DEC-069): "invoice ₹4,265 with CGST + SGST columns"; desktop `InvoiceModal` (features/folio/modals.tsx) has Net / GST rate / CGST / SGST columns and CGST + SGST totals.

Actual: the phone invoice (card per line: Date, Net, GST) and the Android A4 print preview (ISO A4, 1/1) show only Net and a single Tax column and totals "Total including GST ₹4,265"; no CGST or SGST amounts anywhere, only the footnote "GST from the dated GST master (CGST + SGST, illustrative)". Total is correct (4,265 = net 4,000 + GST 265).

Evidence: apk-v10\v10-invoice-gst.png, apk-v10\v10-invoice-print-preview.png.
