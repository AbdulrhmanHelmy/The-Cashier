param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$ChildDirectory
)

$ErrorActionPreference = 'Stop'
$assemblyDirectory = Join-Path $PSScriptRoot "..\src\AlKhayal.Data\bin\$Configuration\net48"
foreach ($name in 'AlKhayal.Core','AlKhayal.Application','System.Data.SQLite','Dapper','AlKhayal.Data') {
    [Reflection.Assembly]::LoadFrom((Join-Path $assemblyDirectory "$name.dll")) | Out-Null
}

function Open-TestDatabase($Path) {
    $connection = [System.Data.SQLite.SQLiteConnection]::new([AlKhayal.Data.Database.Db]::ConnectionStringFor($Path))
    $connection.Open()
    return ,$connection
}

function Invoke-TestSql($Connection, $Sql, $Name) {
    $command = $Connection.CreateCommand()
    try {
        $command.CommandText = $Sql
        if ($Name) { $command.Parameters.AddWithValue("@name", $Name) | Out-Null }
        $command.ExecuteScalar()
    } finally { $command.Dispose() }
}

$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$token = [Threading.CancellationToken]::None
if ($ChildDirectory) {
    $ChildDirectory = (Resolve-Path -LiteralPath $ChildDirectory).Path
    if (!$ChildDirectory.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $ChildDirectory -Leaf) -notmatch '^alk-restore-crash-[0-9a-f]{32}$' -or
        (Get-Item -LiteralPath $ChildDirectory).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw "The child process accepts only the disposable crash-test directory."
    }
    $archive = [IO.File]::ReadAllText((Join-Path $ChildDirectory 'archive-path.txt'))
    $service = [AlKhayal.Data.Backups.SqliteBusinessBackups]::new((Join-Path $ChildDirectory 'target.db'), $null)
    $inputFile = [IO.File]::OpenRead($archive)
    try { $preview = $service.InspectAsync($inputFile, $token).GetAwaiter().GetResult() }
    finally { $inputFile.Dispose() }
    try { $service.RestoreAsync($preview.Confirmation, $token).GetAwaiter().GetResult() }
    finally { $service.Dispose() }
    return
}

$root = Join-Path $temp ('alk-restore-crash-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $source = Join-Path $root 'source.db'
    $target = Join-Path $root 'target.db'
    foreach ($path in $source,$target) {
        [AlKhayal.Data.Database.Migrator]::Run([AlKhayal.Data.Database.Db]::ConnectionStringFor($path))
        $connection = Open-TestDatabase $path
        try {
            $name = if ($path -eq $source) { 'replacement' } else { 'original' }
            Invoke-TestSql $connection 'INSERT INTO Products(Name,Price) VALUES(@name,1), (zeroblob(134217728),2)' $name | Out-Null
        } finally { $connection.Dispose() }
    }

    $service = [AlKhayal.Data.Backups.SqliteBusinessBackups]::new($source, $null)
    try { $archive = $service.CreateAsync((Join-Path $root "Backups")).GetAwaiter().GetResult() }
    finally { $service.Dispose() }
    [IO.File]::WriteAllText((Join-Path $root 'archive-path.txt'), $archive.Path)

    $child = $null
    try {
        $arguments = '-NoProfile -File "{0}" -Configuration {1} -ChildDirectory "{2}"' -f $PSCommandPath,$Configuration,$root
        $child = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru `
            -RedirectStandardError (Join-Path $root 'child-error.txt') -RedirectStandardOutput (Join-Path $root 'child-output.txt')
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $journal = $target + '-journal'
        while (!$child.HasExited -and !(Test-Path -LiteralPath $journal) -and $watch.Elapsed.TotalSeconds -lt 90) {
            Start-Sleep -Milliseconds 10
        }
        if ($child.HasExited -or !(Test-Path -LiteralPath $journal)) {
            throw ('Interrupted restore test did not observe a live rollback journal. ' + (Get-Content -LiteralPath (Join-Path $root 'child-error.txt') -Raw))
        }
        $child.Kill()
        $child.WaitForExit()
    } finally {
        if ($child -and !$child.HasExited) { $child.Kill(); $child.WaitForExit() }
        if ($child) { $child.Dispose() }
    }

    $restart = [AlKhayal.Data.Backups.SqliteBusinessBackups]::new($target, $null)
    try { $restart.Start() }
    finally { $restart.Dispose() }
    if (@(Get-ChildItem -LiteralPath ($target + ".backup-work") -Directory).Count -ne 0) {
        throw "Restart did not clean the interrupted restore staging directory."
    }

    $reopened = Open-TestDatabase $target
    try {
        $original = Invoke-TestSql $reopened 'SELECT Name FROM Products WHERE Id=1'
        $integrity = Invoke-TestSql $reopened 'PRAGMA integrity_check'
        $count = Invoke-TestSql $reopened 'SELECT COUNT(*) FROM Products'
        $emergency = @(Get-ChildItem -LiteralPath (Join-Path $root 'RestoreRecovery') -Filter '*.alkbackup').Count
        if ($original -ne 'original' -or $integrity -ne 'ok' -or $count -ne 2 -or $emergency -ne 1) {
            throw 'SQLite did not recover the original database and emergency archive after process interruption.'
        }
        Write-Output 'Interrupted restore recovered the original database and retained the emergency archive.'
    } finally { $reopened.Dispose() }
} finally {
    [System.Data.SQLite.SQLiteConnection]::ClearAllPools()
    $resolved = (Resolve-Path -LiteralPath $root).Path
    if ($resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path $resolved -Leaf) -match '^alk-restore-crash-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
