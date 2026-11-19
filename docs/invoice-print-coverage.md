# Invoice print access

All actions submit a saved document ID to `POST /api/printing/jobs`. The application service loads the authoritative document, resolves the saved profile and template, and sends it through `NativePrintRenderer` and `WindowsPrinterBackend`. Successful submission means Windows accepted the job; physical output is not verified. The branding preview uses the same renderer with sample document data. Current branding is applied to historical saved invoice values.

| Document | View | Print action | Profile | Limit |
| --- | --- | --- | --- | --- |
| Posted sale, including Touch POS | `sales-management`, `pos` | Existing receipt action | `sale` | Drafts and cancelled sales cannot print. |
| Posted purchase | `purchase-details` | Added top `طباعة الفاتورة` action | `purchase` | Drafts cannot print. Current payable balance is not projected. |
| Sales return | `return-details` | Existing shared action, now labeled consistently | `sales-return` | Return line unit prices are unavailable in the saved return projection. |
| Purchase return | `return-details` | Existing shared action, now labeled consistently | `purchase-return` | Return line unit prices are unavailable in the saved return projection. |
| Delivery sale | Touch POS and `delivery-orders` | Separate `طباعة الدليفري` / `بون` action | `delivery` | Only posted sales with saved delivery data print. There is no separate delivery detail route. |
| Installment sale | `installments` invoice modal | Added action for the linked posted sale | `sale` | Prints the sale invoice, not the contract schedule. Contracts without a linked sale cannot print as invoices. |
| Maintenance/service | Maintenance screens | None | None | No authoritative maintenance print document type or provider exists. |

The shared transport locks a button while submitting and reuses the same operation key after an uncertain network result. The API checks session, CSRF, origin, document type, and screen permissions. The print provider rejects unposted purchase and sale records and never invokes financial posting. Printer assignment, copies, paper, margins, logo, text, and barcode are resolved by the existing saved settings path. Sales invoices retain the four-column thermal layout; delivery tickets use a compact product-and-quantity layout. Windows 7 and XP-80C paper behavior still require physical device qualification.

## Separate delivery ticket

When `AutoPrintSale` and the saved `AutoPrintDelivery` setting are enabled, posting a delivery sale requests two independent, idempotent jobs: `sale` and `delivery`. Each uses its assigned Windows printer and saved native template. An uncertain submission is not automatically retried. The delivery ticket has the saved customer phone, address, courier snapshot, instructions, products and quantities, delivery fee, and the current saved invoice outstanding amount. It omits product prices and the sales invoice financial summary. A fully paid invoice shows zero outstanding. The database has no separate cash-on-delivery assignment field, so the outstanding amount is labeled as remaining to collect; it must not be interpreted as an independent courier settlement record.

Touch POS keeps the last posted delivery ID and number in the current cashier state after the cart clears. Its direct button is disabled for an unrelated active invoice or draft. Delivery Orders offers separate invoice and ticket print actions and navigation to the read-only sales invoice. The compact table truncates long addresses while retaining the full value as accessible text.
