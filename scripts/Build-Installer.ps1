[CmdletBinding()]
param([string]$Iscc)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Iscc) { $Iscc = (& "$PSScriptRoot/Restore-ReleaseTools.ps1").Iscc }
$archive = Join-Path $root 'artifacts/NexusExplorer_new.zip'
$output = Join-Path $root 'artifacts/releases'
$work = Join-Path $root ('artifacts/.installer-' + [Guid]::NewGuid().ToString('N'))
try {
    # Only extract the verified portable archive, never package a user's runtime folder.
    $python = Get-Command python -ErrorAction Stop
    & $python.Source -c 'import sys; sys.path.insert(0,sys.argv[1]); from package_portable import inspect_archive; inspect_archive(sys.argv[2])' $PSScriptRoot $archive
    if ($LASTEXITCODE -ne 0) { throw 'Portable archive validation failed.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $work
    $metadata = Get-Content -LiteralPath (Join-Path $work 'release.json') -Raw | ConvertFrom-Json
    if ($metadata.version -notmatch '^\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid version.' }
    [IO.Directory]::CreateDirectory($output) | Out-Null
    & $Iscc /Qp "/DPayload=$work" "/DAppVersion=$($metadata.version)" "/DOutput=$output" "$PSScriptRoot/NexusExplorer.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Copy-Item -LiteralPath $archive -Destination (Join-Path $output "NexusExplorer-$($metadata.version)-win-x64-Portable.zip") -Force
    $files = @("NexusExplorer-$($metadata.version)-win-x64-Portable.zip", "NexusExplorer-$($metadata.version)-win-x64-Setup.exe")
    $lines = foreach ($name in $files) { "$((Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256).Hash.ToLowerInvariant())  $name" }
    $lines | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Installer and portable assets: $output"
}
finally {
    $expected = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if ([IO.Path]::GetFullPath($work).StartsWith($expected, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($work) -match '^\.installer-[0-9a-f]{32}$' -and (Test-Path -LiteralPath $work)) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
