# Operation audit log

The administrator-only log is a unified activity index for authenticated API requests (reads and mutations), plus successful login/session establishment. Each entry stores the actor's ID and name snapshots, HTTP method, API path, result status and UTC timestamp. The Settings hub page displays the timestamp in local time and supports search, date and outcome filters with bounded paging.

The API audit handler reads the session before dispatch so logout remains attributed to the user; successful login is attributed from its newly issued session cookie. It does not save request bodies, passwords, CSRF/session tokens, or query strings. Anonymous failed logins cannot be attributed and are not included. The log-list endpoint is excluded to prevent recursive entries. Requests that do not pass through the API (for example, purely local UI changes or direct database edits) cannot be attributed by this log.

The log table is append-only and actor names are snapshotted so history remains readable if a user record is removed. The handler appends after the API response to avoid converting a completed financial operation into an ambiguous retry if logging storage fails; such failures are written to the server trace. Financial source ledgers and domain audits committed in the business transaction remain authoritative for money and stock changes.

The log covers requests through the authenticated local/LAN API. It does not claim to record direct database edits, operating-system activity, or internal code paths that do not pass through that API.
