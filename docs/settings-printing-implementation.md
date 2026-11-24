# Settings and Printing Settings implementation

21 November 2026 · The Cashier 1.1.0

## Independent reads

The managed page previously waited on one promise group for saved printing settings, company data and Windows device discovery. Saved profile cards, all nine assignments and the designer actions now render as soon as the printing-settings request succeeds. Barcode settings start their own read at mount. Company data loads separately and is required only for branded receipt preview. Errors and retries appear in the area they affect. A failed printing-settings read leaves the profile and assignment save controls absent.

The Windows refresh action shares an outstanding discovery promise across views. Each view tracks its own pending state and presents loading, refresh, empty, success and failure feedback. The API client's existing `skipPageTrack` option prevents workspace freshness checks from starting extra discovery. The advanced editor uses the manager's current discovery result and the same refresh action.

A retained workspace tab can settle data while detached. Async handlers use the workspace's existing current-view generation check, so a closed or replaced view cannot apply late results. A replacement view can join the pending scan. Subsequent refresh starts a fresh request after the previous request settles.

## Settings and drafts

Discovery updates Windows status text and device options in place. The selected name remains available as an option when unverified or absent. It is retained independently of the Windows default. Refresh keeps the barcode editor, advanced editor, job history, profile dialog and the rest of their inputs in place. Device status describes enumeration results. The saved reload action refreshes application printing configuration and leaves Windows discovery to its own command.

The profile dialog builds a full save candidate from the loaded configuration and current draft. Save locks its submitted controls, prevents duplicate requests, and keeps the draft open with an error if the request fails. The assignment save continues to submit the complete settings object, including existing templates and profiles. Profile save retains the same device and paper checks and the existing server validation. Existing profile IDs, legacy printer names, document template behavior and the label assignment and design API remain in use.

## UI and accessibility

- Registered profiles are labeled separately from discovered devices and document assignments. Assignment selects are associated with their document labels.
- The printer dialog groups device identity, paper and branding fields, puts preview source ahead of the action, and provides a shortcut to the labeled preview region. Initial focus goes to the profile name. Validation focuses the relevant field and the error stays in the modal footer.
- The visual designer retains its viewing zoom and distinct sample and saved-document paths. The preview region can receive keyboard focus. Display adjustments act on browser presentation.
- Scale text, numeric and select fields have native labels. Light, Dark and System use the existing Appearance buttons with synchronized `aria-pressed` state.

## Changed files

| File | Change |
| --- | --- |
| `frontend/js/modules/printing.js` | Independent reads, discovery request coordination, retained-view handling, in-place device updates, profile save candidate, modal and preview UI |
| `frontend/js/modules/labels.js` | Independent label settings state and retry, in-place device options, retained preview updates |
| `frontend/css/printing.css` | Dialog spacing, preview area and error placement |
| `frontend/js/modules/business-activities.js` | Scale field labels |
| `frontend/js/modules/general-settings.js` | Theme selection semantics |
| `frontend/tests/printing-tests.js` | Controlled responses, retained and replaced views, save and draft regressions |
| `frontend/tests/settings-printing-browser.cjs` | Edge fixtures, timing, screenshot and interaction matrix |

The related reports are [performance](settings-printing-performance-results.md), [regression QA](settings-printing-regression-qa.md) and [defect register](settings-printing-defect-register.md). The checkout had existing worktree changes from earlier stages, including native and backend files. This task's production edits are the five frontend files listed above. It uses the same routes, API contracts and backend validation. Driver work reduction and server-side cancellation are outside the approved frontend boundary. Browser APIs used here are available in Chromium 109, with runtime qualification outside this run.
