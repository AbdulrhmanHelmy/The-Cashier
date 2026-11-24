param(
    [Parameter(Mandatory=$true)][string]$Database,
    [Parameter(Mandatory=$true)][string]$EndDate,
    [ValidateSet('verify','replace','validate-stage','publish-stage','publish-refreshed-stage','supplement-stage','supplement-live','check-reports')][string]$Mode = 'verify',
    [int]$Days = 365,
    [string]$RunDirectory
)

$ErrorActionPreference = 'Stop'
$databaseFile = Get-Item -LiteralPath $Database
if (!$RunDirectory) {
    if ($Mode -in 'validate-stage','publish-stage','publish-refreshed-stage','supplement-stage','supplement-live') { throw 'Supply the original RunDirectory for the simulation.' }
    $RunDirectory = Join-Path $databaseFile.DirectoryName ("simulation-" + [Guid]::NewGuid().ToString('N'))
}
$sourceRoot = (Get-Item -LiteralPath (Join-Path $PSScriptRoot '../../src/AlKhayal.Data')).FullName
$engine = Join-Path $PSScriptRoot ('obj/Engine-' + [Guid]::NewGuid().ToString('N'))
$compileRoot = Join-Path $engine 'source'
$output = Join-Path $engine 'bin'
foreach ($source in (Get-ChildItem -LiteralPath $sourceRoot -Directory | Where-Object Name -NotIn 'obj','bin')) {
    Get-ChildItem -LiteralPath $source.FullName -Recurse -Filter '*.cs' | ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.Length + 1)
        $destination = Join-Path $compileRoot $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        $contents = [IO.File]::ReadAllText($_.FullName)
        if ($source.Name -ne 'Licensing') {
            $contents = $contents -creplace '(?:System\.)?\bDateTime\.UtcNow\b', 'global::SimulationClock.UtcNow'
            $contents = $contents -creplace '(?:System\.)?\bDateTime\.Now\b', 'global::SimulationClock.LocalNow'
            $contents = $contents -creplace '(?:System\.)?\bDateTime\.Today\b', 'global::SimulationClock.LocalNow.Date'
            if ($source.Name -in 'Sales','Purchasing','Inventory') {
                $contents = $contents.Replace('Guid.NewGuid()', 'global::SimulationClock.NextDocumentId()')
            }
            if ($_.Name -eq 'SqliteTreasuryStore.cs') {
                # The stage starts with empty accounts. Validation recomputes every running balance from signed movements.
                $runningSum = "SELECT SUM\(CASE WHEN m\.Type='deposit' THEN m\.AmountScaled ELSE -m\.AmountScaled END\)\s+FROM TreasuryMovements m WHERE m\.AccountId=a\.Id"
                $contents = $contents -creplace $runningSum, 'SELECT m.BalanceAfterScaled FROM TreasuryMovements m WHERE m.AccountId=a.Id ORDER BY m.Id DESC LIMIT 1'
            }
        }
        [IO.File]::WriteAllText($destination, $contents)
    }
}
dotnet build (Join-Path $PSScriptRoot 'TheCashier.Simulation.csproj') -c Release --output $output "-p:SimulationDataSource=$compileRoot"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Write-Host "Run directory $RunDirectory"
& (Join-Path $output 'TheCashier.Simulation.exe') $databaseFile.FullName $EndDate $Mode $Days $RunDirectory
exit $LASTEXITCODE
