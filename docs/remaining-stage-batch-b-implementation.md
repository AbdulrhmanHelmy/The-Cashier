# Batch B implementation

Payroll displays a loading message and clears the old history when an employee or period changes. The latest outstanding request alone can populate the quote and history or show an error. This guard covers both employee changes and repeated period changes for one employee. Existing payroll calculation and submission expressions are preserved. See the [defect register](remaining-stage-batch-b-defect-register.md).

Installment collection resets the unsent amount when its customer changes. A delayed success refreshes the originating customer's displayed contracts when applicable and preserves a different selection or newly edited amount. The submitted payment values and API endpoint are unchanged.
