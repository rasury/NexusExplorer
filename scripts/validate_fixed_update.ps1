param([Parameter(Mandatory = $true)][string]$PublishedDirectory,
    [string]$OutputDirectory = 'artifacts/NexusExplorer_new')
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskFixed = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { [IO.Path]::GetFullPath((Join-Path $taskRepository $OutputDirectory)) }
$taskSettingsAlreadyExisted = Test-Path -LiteralPath (Join-Path $taskFixed 'appsettings.json')
function Get-UserSnapshot {
    $taskItems = @()
    foreach ($taskName in @('data', 'Storage', 'logs')) {
        $taskFolder = Join-Path $taskFixed $taskName
        if (-not (Test-Path -LiteralPath $taskFolder)) { continue }
        $taskItems += Get-Item -LiteralPath $taskFolder
        $taskItems += Get-ChildItem -LiteralPath $taskFolder -Recurse -Force
    }
    if ($taskSettingsAlreadyExisted -and (Test-Path -LiteralPath (Join-Path $taskFixed 'appsettings.json'))) {
        $taskItems += Get-Item -LiteralPath (Join-Path $taskFixed 'appsettings.json')
    }
    foreach ($taskItem in $taskItems | Sort-Object FullName) {
        $taskRelative = $taskItem.FullName.Substring($taskFixed.TrimEnd('\', '/').Length + 1)
        $taskHash = $null
        if (-not $taskItem.PSIsContainer -and -not $taskRelative.StartsWith('Storage\')) {
            $taskHash = (Get-FileHash -LiteralPath $taskItem.FullName).Hash
        }
        [pscustomobject]@{ path = $taskRelative; directory = $taskItem.PSIsContainer;
            length = $taskItem.Length; modified = $taskItem.LastWriteTimeUtc.Ticks; hash = $taskHash }
    }
}
$taskBefore = @(Get-UserSnapshot) | ConvertTo-Json -Depth 4
& (Join-Path $taskRepository 'scripts/Install-Portable.ps1') -PublishedDirectory $PublishedDirectory -OutputDirectory $taskFixed
$taskAfter = @(Get-UserSnapshot) | ConvertTo-Json -Depth 4
if ($taskBefore -cne $taskAfter) { throw '用户目录快照不同，数据保留验证失败。' }
Write-Output 'PASS: 固定目录实际更新后，data/Storage/logs/配置内容或元数据完全保持，未运行软件或打开数据库迁移。'

