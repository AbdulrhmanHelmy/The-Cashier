# Touch POS cart layout and invoice-only discounts

Date: 2026-11-08. Implemented directly in Codex; no delegation, UI sessions, browser automation, live database access, commit or push.

## Root cause and changes

The existing four-column item grid nested price/discount controls under the product name. A later Touch rule allowed unlimited name wrapping, and a workspace breakpoint promoted details to a full-width row. This produced tall, scattered cards, especially with tracking and manual-price controls.

`frontend/js/modules/sales.js` now renders five cells for every Touch context: details, quantity, unit price, total, remove. `frontend/css/modules.css` bounds names, metadata and numeric cells, reserves a useful desktop cart width and uses actual-cart-width fallbacks. Product metadata expands with native keyboard-accessible details/summary; full names remain in the DOM/title. Quantity, weighted input/dialog, selected-unit/automatic pack pricing, monetary calculation and override handlers remain unchanged.

Touch has no editable line discounts. New lines already initialize the API field to zero. Existing nonzero line discounts are disclosed in metadata; a summary message explains that the record is view-only in Touch. Lock, consent and payload guards prevent modifying/saving/posting/correcting it. The explicit non-destructive path is to retain the saved original and start an independent new invoice. Saved totals and all historical fields remain intact. Fixed/percentage invoice discounts retain their existing validation and tax calculation. No production backend, API, database or FIFO change is included.

Changed files for this task (all pre-existing uncommitted work was retained):

- `frontend/js/modules/sales.js`
- `frontend/css/modules.css`
- `frontend/tests/touch-pos-invoice-tests.js`
- `frontend/tests/touch-pos-layout-tests.js`
- `frontend/tests/manual-weight-tests.js`
- `tests/AlKhayal.IntegrationTests/ManualWeightTests.cs` (local-day report test correction only)
- `docs/design.md`
- `docs/testing.md`
- `docs/qa/touch-pos-cart-layout.md`

## Automated verification

```powershell
node frontend/tests/touch-pos-invoice-tests.js
node frontend/tests/touch-pos-layout-tests.js
node frontend/tests/run-tests.js
# Passed; full suite includes 131 registered screens.

$taskSyntaxFiles=@(rg --files frontend/js -g '*.js')+@('frontend/tests/touch-pos-invoice-tests.js','frontend/tests/touch-pos-layout-tests.js','frontend/tests/manual-weight-tests.js')
foreach($taskSyntaxFile in $taskSyntaxFiles){node --check $taskSyntaxFile; if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}}
# Passed: 47 JavaScript files.

# Set a unique temporary fallback; fixtures also create disposable database files.
$env:ALKHAYAL_DATABASE_PATH=Join-Path $env:TEMP ('alkhayal-cart-guard-'+[guid]::NewGuid().ToString('N')+'.db')
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore -c Release -m:1 -p:OutputPath=bin/TouchCart/ --filter "FullyQualifiedName~TouchPosInvoiceEditingTests|FullyQualifiedName~SalesPersistenceTests|FullyQualifiedName~PricingFifoInvariantTests|FullyQualifiedName~ManualWeightTests|FullyQualifiedName~InventoryFifoTests|FullyQualifiedName~SalesStockFloorTests|FullyQualifiedName~SalesTreasuryTests|FullyQualifiedName~SalesCancellationValuationTests|FullyQualifiedName~Touch_pos_invoice_http|FullyQualifiedName~PaymentInstrumentTests|FullyQualifiedName~DeliveryApiTests|FullyQualifiedName~RequiredShiftTests" --logger "console;verbosity=minimal"
# Final rerun passed: 77, failed: 0, skipped: 0.

dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --no-restore -c Release -m:1 -p:OutputPath=bin/TouchCartUnit/ --logger "console;verbosity=minimal"
# Passed: 282, failed: 0, skipped: 0.

dotnet build AlKhayal.slnx --no-restore -c Release -m:1 -p:OutputPath=bin/TouchCartRelease/
# Passed: 0 warnings, 0 errors.
git -c core.safecrlf=false diff --check
# Passed after removing trailing blank lines in the appended documentation.
```

The first integration run had 76 passes and one failure at `ManualWeightTests.cs:163`: Cairo was on November 8 while UTC was still November 7; a UTC-day report excluded current postings when filtered with the local date. The test now injects the local business timezone matching its dates. This preserves every existing assertion and changes no production calculation.

## Manual acceptance still required

No automated geometry or native visual pass is claimed. The owner should check 1024×768, 1280×720, 1366×768, 1440×900 and 1920×1080 with expanded/collapsed sidebar, light/dark themes and applicable Windows DPI. Inspect new, view-only, resumed and posted invoices; long Arabic names, many rows, large totals, fractional weights, selected selling units, batches/expiry and serial/IMEI. Confirm compact alignment, metadata keyboard expansion, first/last item reachability, summary and New Invoice/Save Draft/Complete Sale accessibility. At genuinely narrow cart widths verify the intentional fallback keeps every control accessible. Historical line-discount records must show the explanation/unchanged totals and remain view-only; ordinary drafts must retain the view → resume → same-ID save/post workflow.

The source uses Chromium 109-compatible HTML/CSS/JavaScript without new dependencies. Windows 7/legacy WebView2 runtime qualification and physical-device acceptance are not established by these tests.
