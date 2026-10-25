param([Parameter(Mandatory=$true)][string]$SetupPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$id = '{F47D2E21-63F4-44CB-9B91-6CEB32B0459B}_is1'
foreach ($hive in @('HKCU:', 'HKLM:')) {
    foreach ($prefix in @('Software/Microsoft/Windows/CurrentVersion/Uninstall', 'Software/WOW6432Node/Microsoft/Windows/CurrentVersion/Uninstall')) {
        if (Test-Path "$hive/$prefix/$id") { throw 'An installed copy exists; do not run isolated smoke test on this machine.' }
    }
}
$work = Join-Path $root ('artifacts/.installer-smoke-' + [Guid]::NewGuid().ToString('N'))
$installed = $false
function Install {
    $p = Start-Process -FilePath $SetupPath -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',"/DIR=`"$work`"") -WindowStyle Hidden -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "Installer exited $($p.ExitCode)" }
}
try {
    Install
    $installed = $true
    if (!(Test-Path "$work/NexusExplorer.exe") -or !(Test-Path "$work/licenses/NexusExplorer-source.zip")) { throw 'Missing installed files.' }
    foreach ($name in @('data/nexus.db', 'Storage/sample.txt', 'logs/sample.log', 'cache/sample.bin')) {
        $path = Join-Path $work $name
        [IO.Directory]::CreateDirectory((Split-Path $path -Parent)) | Out-Null
        [IO.File]::WriteAllText($path, 'smoke-test-user-content')
    }
    [IO.File]::WriteAllText("$work/appsettings.json", '{"smoke_test":"preserve"}')
    Install
    if ([IO.File]::ReadAllText("$work/appsettings.json") -ne '{"smoke_test":"preserve"}') { throw 'Upgrade overwrote config.' }
    $p = Start-Process -FilePath "$work/unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw 'Uninstall failed.' }
    $installed = $false
    if (Test-Path "$work/NexusExplorer.exe") { throw 'Uninstall left the executable.' }
    foreach ($name in @('data/nexus.db', 'Storage/sample.txt', 'logs/sample.log', 'cache/sample.bin')) {
        if ([IO.File]::ReadAllText((Join-Path $work $name)) -ne 'smoke-test-user-content') { throw "User file changed: $name" }
    }
    if ([IO.File]::ReadAllText("$work/appsettings.json") -ne '{"smoke_test":"preserve"}') { throw 'Uninstall removed config.' }
    Write-Output 'PASS: install, upgrade, uninstall; config/database/Storage/logs/cache preserved.'
}
finally {
    # Leave failures available for diagnosis. Never delete a live registered install.
    $prefix = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (!$installed -and [IO.Path]::GetFullPath($work).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($work) -match '^\.installer-smoke-[0-9a-f]{32}$' -and (Test-Path -LiteralPath $work)) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
