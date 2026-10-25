param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
function Get-NativeHash([string]$Path) {
    $taskStream = [IO.File]::OpenRead($Path)
    $taskSha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($taskSha.ComputeHash($taskStream))).Replace('-', '').ToLowerInvariant() }
    finally { $taskStream.Dispose(); $taskSha.Dispose() }
}
$taskRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$taskManifest = Get-Content -LiteralPath (Join-Path $taskRoot 'scripts/mpv-runtime.json') -Raw | ConvertFrom-Json
$taskNative = Join-Path $taskRoot 'native/mpv/win-x64'
for ($taskAncestor = $taskNative; $taskAncestor; $taskAncestor = [IO.Path]::GetDirectoryName($taskAncestor)) {
    if ((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'mpv SDK 路径包含联接点，停止还原。' }
}
$taskDll = Join-Path $taskNative $taskManifest.dll
if ((Test-Path -LiteralPath $taskDll) -and (Get-NativeHash $taskDll) -eq $taskManifest.dllSha256) {
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/mpv-runtime.json') -Destination (Join-Path $taskNative 'runtime-manifest.json') -Force
    return
}
$taskCache = Join-Path $taskRoot 'native/mpv/cache'
for ($taskAncestor = $taskCache; $taskAncestor; $taskAncestor = [IO.Path]::GetDirectoryName($taskAncestor)) {
    if ((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'SDK 缓存路径包含联接点，停止还原。' }
}
New-Item -ItemType Directory -Path $taskCache -Force | Out-Null
$taskArchive = Join-Path $taskCache ([IO.Path]::GetFileName(([uri]$taskManifest.url).AbsolutePath))
if (-not (Test-Path -LiteralPath $taskArchive)) {
    Write-Host '正在下载固定版本 mpv x64 开发包……'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $taskDownloader = New-Object Net.WebClient
    try { $taskDownloader.DownloadFile($taskManifest.url, ($taskArchive + '.download')) }
    finally { $taskDownloader.Dispose() }
    Move-Item -LiteralPath ($taskArchive + '.download') -Destination $taskArchive -Force
}
if ((Get-NativeHash $taskArchive) -ne $taskManifest.archiveSha256) { throw 'mpv 开发包哈希不匹配，停止还原。' }
$taskStaging = Join-Path $taskCache ('restore-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskStaging | Out-Null
try {
    & tar.exe -xf $taskArchive -C $taskStaging $taskManifest.dll
    if ($LASTEXITCODE -ne 0) { throw '无法解压 mpv；需要支持 7z 的 Windows tar.exe。' }
    $taskExtracted = Join-Path $taskStaging $taskManifest.dll
    if ((Get-NativeHash $taskExtracted) -ne $taskManifest.dllSha256) { throw 'mpv DLL 哈希不匹配。' }
    New-Item -ItemType Directory -Path $taskNative -Force | Out-Null
    Move-Item -LiteralPath $taskExtracted -Destination $taskDll -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/mpv-runtime.json') -Destination (Join-Path $taskNative 'runtime-manifest.json') -Force
}
finally {
    $taskStagingFull = [IO.Path]::GetFullPath($taskStaging)
    if (-not $taskStagingFull.StartsWith([IO.Path]::GetFullPath($taskCache).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($taskStagingFull) -notmatch '^restore-[0-9a-f]{32}$') { throw 'SDK 临时目录边界检查失败。' }
    if (Test-Path -LiteralPath $taskStagingFull) {
        if ((Get-Item -LiteralPath $taskStagingFull -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'SDK 临时目录变成链接，停止清理。' }
        Remove-Item -LiteralPath $taskStagingFull -Recurse -Force
    }
}
Write-Host '已还原并校验 mpv x64 原生库。'
