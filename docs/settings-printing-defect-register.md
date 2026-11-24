# Settings and Printing Settings defect register

| Status | Observation and reproduction | Regression evidence |
| --- | --- | --- |
| FIXED | Put a 1,250 or 3,000 ms hold on discovery. The previous page delayed saved controls until discovery completed. | Before/after Edge timings and simulated pending screenshot. Controlled test checks nine assignments and save availability during pending discovery. |
| FIXED | Dispatch repeated refresh clicks while discovery is pending. The previous page made duplicate requests. | Edge request counts and controlled pending request test. A failed request can be retried. |
| FIXED | Switch to another tab while discovery runs. The previous retained tab could show its original loading card after return. | Edge active, inactive-success and inactive-failure scenarios, close/reopen, and deterministic disposed-view checks. Single-flight prevents independent overlapping discovery responses. |
| FIXED | Return a discovery error or remove a previously selected name from a simulated result. The prior display conflated failed discovery with absence. | Failure/empty screenshots, offline card, and stable saved selection, all nine assignments and full request body. |
| FIXED | Edit paper or label dimensions, then refresh. Previously the manager rebuilt child editors. | Edge preserves draft name, selected device, receipt width and footer, and label custom width through failure, empty and success. |
| FIXED | Reject profile save or double-click Save. | Controlled save rejection, one PUT, retained draft and clean reopen after cancel. |
| FIXED | Open the printer dialog at 1024×768 and omit paper. | Both themes show grouped controls and reachable footer. Paper receives validation focus. Escape restores the trigger. |
| FIXED | Inspect the document assignment and scale form labels, then switch Appearance options from the keyboard. | Edge verifies native scale labels and the selected theme's pressed state. |
| ALREADY FIXED | Historical preview lacked viewing zoom. The current visual designer already supplied it. | Edge changes viewing zoom to 125% and verifies image width and a saved-document fixture preview. |
| ALREADY FIXED | Historical backup external destination messaging and stale permissions selection. | Existing Stage C and frontend regressions. |
| BLOCKED | Reducing per-device Windows driver queries or canceling a running spooler operation requires backend and hardware changes. | Historical read-only probes establish the time spent there. The browser shares only the in-flight request. |
| NOT TESTED | Real WebView2 navigation, physical printing, real saved sale and purchase receipt image fidelity, and legacy Windows runtime behavior. | Edge uses isolated settings and PNG fixtures. Automated test runs did not dispatch a physical job. |

See the [QA matrix](settings-printing-regression-qa.md) and [performance data](settings-printing-performance-results.md).
