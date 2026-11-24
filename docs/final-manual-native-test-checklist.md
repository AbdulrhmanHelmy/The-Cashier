# Final manual native test checklist

Use the Release WinForms executable, an isolated WebView2 profile and a disposable application database. Use synthetic records and disposable administrator and limited cashier accounts. Record OS, WebView2 version, display scaling and case result for each run.

- [ ] Start the native host, verify login and error recovery, then sign in as the restricted cashier and confirm effective routes and actions. Check Light and Charcoal Dark, Arabic RTL, keyboard focus, native dialogs and accessibility at 100%, 125% and 150% scaling.
- [ ] Add products by keyboard and touch, exercise rapid searches, quantities, removal, stale results and unknown barcode. Record simulated scanner input separately from the physical scanner.
- [ ] Run cash and credit checkout in an open shift. Test duplicate input, backend rejection, deliberate lost response in an isolated test and re-open the confirmed invoice. Verify invoice number, method, total, receipt identity and the new cart.
- [ ] Search exact invoice numbers, open successive records, inspect PDF and receipt preview, open a report-linked invoice and the eligible return entry. Verify cancellation reason, safe Escape, permitted cancellation on a disposable sale and rejection when prohibited.
- [ ] Run 20 end-to-end Delivery create → correct → save → reopen trials. Include assigned and unassigned deliveries, status transitions, refresh, workspace switch, return from another module, rapid edit and desktop restart. Record original and corrected invoice IDs, customer, address, driver, HTTP/error status and frequency. Check eligible original open shift, restricted roles, unavailable drivers and stale revisions.
- [ ] Test driver create/edit, failed save, duplicate click, dismissal, search and order links. Verify that failures preserve typed fields and reopen works.
- [ ] Qualify Windows 7 SP1 only with the approved compatible runtime. Run the physical printer cases only after approval and record paper type, printer, preview and actual output as separate checks.
