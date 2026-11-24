param(
    [Parameter(Mandatory=$true)][string]$RunDirectory,
    [string]$Database = (Join-Path $env:LOCALAPPDATA 'TheCashier/thecashier.db'),
    [string]$From = '2025-11-24',
    [string]$To = '2026-11-23'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$baseUri = 'http://127.0.0.1:5138'
$secretFile = Join-Path $RunDirectory 'admin-password.dpapi'
$secret = [Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes($secretFile), $null,
    [Security.Cryptography.DataProtectionScope]::CurrentUser
)
$loginBody = @{ username = 'nakheel-admin'; password = [Text.Encoding]::UTF8.GetString($secret) } | ConvertTo-Json
try {
    $login = Invoke-RestMethod "$baseUri/api/auth/login" -Method Post -ContentType 'application/json' -Body $loginBody -SessionVariable session -TimeoutSec 20
} finally {
    [Array]::Clear($secret, 0, $secret.Length)
    $loginBody = $null
}
if (!$login.success -or $login.data.role -ne 'administrator') { throw 'Administrator login failed.' }
Write-Output 'Verified administrator login over the running desktop HTTP API.'

$search = [Uri]::EscapeDataString('أرز')
$range = "from=$From&to=$To"
$last = [DateTime]::ParseExact($To, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture).AddDays(-1)
$daily = $last.ToString('yyyy-MM-dd')
$month = $last.ToString('yyyy-MM-01')
$checks = [ordered]@{
    'Database path' = '/api/settings/database-info'
    'Company profile' = '/api/settings/company'
    'Catalog page' = '/api/catalog/products?page=2&pageSize=25'
    'Catalog Arabic search' = "/api/catalog/products?search=$search"
    'Catalog category' = '/api/catalog/products?categoryId=1'
    'Barcode lookup' = '/api/catalog/barcodes/lookup?code=2200000000019'
    'Customer search' = '/api/customers/search?query=CU-00001'
    'Supplier lookup' = '/api/suppliers/directory-record/1'
    'Sales history pagination' = "/api/reports/sales?$range&page=2"
    'Purchase history' = "/api/reports/purchases?$range"
    'Return history' = "/api/reports/returns?$range"
    'Daily sales summary' = "/api/reports/sales?from=$daily&to=$daily"
    'Monthly financial summary' = "/api/reports/net-profit?from=$month&to=$daily&breakdown=daily"
    'Stock valuation' = '/api/reports/inventory-valuation'
    'Stock movements' = "/api/reports/inventory-movements?$range"
    'Low stock' = '/api/reports/low-stock'
    'Stock and expiry alerts' = '/api/alerts'
    'Delivery pagination' = '/api/delivery/orders?page=2&pageSize=20'
    'Treasury accounts' = '/api/treasury/accounts'
    'Expense vouchers' = '/api/expenses/vouchers'
    'Employee directory' = '/api/workforce/employees'
    'Payroll history' = '/api/workforce/payroll'
    'Installment receivables' = '/api/treasury/installment-receivable'
    'Maintenance tickets' = '/api/maintenance/tickets'
    'Senior cashier grants' = '/api/users/2/screen-permissions'
    'Standard cashier grants' = '/api/users/10/screen-permissions'
    'Annual financial summary' = "/api/reports/net-profit?$range&breakdown=monthly"
    'Dashboard overview' = "/api/reports/overview?$range"
    'Product profitability' = "/api/reports/product-profit?$range"
}
$failed = 0
try {
    foreach ($entry in $checks.GetEnumerator()) {
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        try {
            $result = Invoke-WebRequest ($baseUri + $entry.Value) -WebSession $session -TimeoutSec 60 -UseBasicParsing
            $stopwatch.Stop()
            $envelope = $result.Content | ConvertFrom-Json
            if (!$envelope.success) { throw "API error $($envelope.error.code)" }
            if ($entry.Key -eq 'Database path' -and [IO.Path]::GetFullPath($envelope.data.databasePath) -ne [IO.Path]::GetFullPath($Database)) {
                throw 'The running application is using a different database.'
            }
            if ($entry.Key -eq 'Company profile' -and $envelope.data.businessName -ne 'النخبة ماركت') {
                throw 'The running application returned an unexpected company profile.'
            }
            if ($entry.Key -eq 'Catalog page' -and ($envelope.data.totalCount -lt 5000 -or @($envelope.data.items).Count -ne 25)) {
                throw 'The running application did not return the generated catalog.'
            }
            if ($entry.Key -eq 'Sales history pagination' -and ($envelope.data.count -lt 35000 -or @($envelope.data.items).Count -ne 20)) {
                throw 'The running application did not return the generated sales history.'
            }
            if ($entry.Key -like '*cashier grants') {
                $allowed = @($envelope.data | Where-Object isEffective | Select-Object -ExpandProperty screenId)
                if ('pos' -notin $allowed -or 'settings-program' -in $allowed -or
                    (($entry.Key -eq 'Senior cashier grants') -ne ('returns-overview' -in $allowed))) {
                    throw 'Persisted access did not match the expected cashier role.'
                }
            }
            [pscustomobject]@{ Check = $entry.Key; Status = 'OK'; Milliseconds = $stopwatch.ElapsedMilliseconds; Bytes = $result.RawContentLength }
        } catch {
            $failed++
            [pscustomobject]@{ Check = $entry.Key; Status = $_.Exception.Message; Milliseconds = $stopwatch.ElapsedMilliseconds; Bytes = 0 }
        }
    }
} finally {
    try {
        Invoke-RestMethod "$baseUri/api/auth/logout" -Method Post -Headers @{ 'X-CSRF-Token' = $login.data.csrfToken } -WebSession $session -TimeoutSec 20 | Out-Null
    } catch {
        Write-Warning "The verification session could not be closed through the HTTP API."
        $failed++
    }
}
if ($failed) { throw "$failed desktop API checks failed." }
