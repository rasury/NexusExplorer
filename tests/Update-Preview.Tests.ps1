$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskFixture = Join-Path $taskRepository ('artifacts/update-validation/' + [Guid]::NewGuid().ToString('N'))
$taskSource = Join-Path $taskFixture 'published'
$taskOutput = Join-Path $taskFixture 'fixed'
function Write-Fixture([string]$Relative, [string]$Text) {
    $taskPath = Join-Path $taskFixture $Relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskPath)) | Out-Null
    [IO.File]::WriteAllText($taskPath, $Text)
}
function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw $Message }
}
Write-Fixture 'published/NexusExplorer.exe' 'NEW EXE'
Write-Fixture 'published/native.dll' 'NEW DLL'
Write-Fixture 'published/release.json' '{"version":"NEW"}'
Write-Fixture 'published/libvlc/win-x64/libvlc.dll' 'NEW VLC'
Write-Fixture 'published/libvlc/win-x64/libvlccore.dll' 'NEW VLC CORE'
Write-Fixture 'published/Storage/keep.txt' 'POISON'
Write-Fixture 'published/data/nexus.db' 'POISON'
Write-Fixture 'published/logs/keep.log' 'POISON'
Write-Fixture 'published/appsettings.json' 'POISON'
Write-Fixture 'published/libvlc/private-db.dll' 'POISON'
Write-Fixture 'published/libvlc/private-db.dll-wal' 'POISON'
Write-Fixture 'published/libvlc/user-storage/keep.dll' 'POISON'
Write-Fixture 'fixed/NexusExplorer.exe' 'OLD EXE'
Write-Fixture 'fixed/native.dll' 'OLD DLL'
Write-Fixture 'fixed/Storage/keep.txt' 'MY STORAGE'
Write-Fixture 'fixed/data/nexus.db' 'MY DB'
Write-Fixture 'fixed/data/nexus.db-wal' 'MY WAL'
Write-Fixture 'fixed/data/nexus.db-shm' 'MY SHM'
Write-Fixture 'fixed/data/ui-state.json' 'MY UI STATE'
Write-Fixture 'fixed/logs/keep.log' 'MY LOG'
Write-Fixture 'fixed/libvlc/private-db.dll' 'MY CUSTOM DB'
Write-Fixture 'fixed/libvlc/private-db.dll-wal' 'MY CUSTOM WAL'
Write-Fixture 'fixed/libvlc/user-storage/keep.dll' 'MY CUSTOM STORAGE'
Write-Fixture 'fixed/appsettings.json' (@{
    Database = @{ Path = 'libvlc/private-db.dll' }
    Storage = @{ RootPath = 'libvlc/user-storage' }
    Logging = @{ Directory = '' }
} | ConvertTo-Json -Depth 4)
$taskProtected = @('Storage/keep.txt', 'data/nexus.db', 'data/nexus.db-wal', 'data/nexus.db-shm', 'data/ui-state.json', 'logs/keep.log', 'appsettings.json', 'libvlc/private-db.dll', 'libvlc/private-db.dll-wal', 'libvlc/user-storage/keep.dll')
$taskSnapshot = @{}
foreach ($taskRelative in $taskProtected) {
    $taskPath = Join-Path $taskOutput $taskRelative
    $taskSnapshot[$taskRelative] = @((Get-FileHash -LiteralPath $taskPath).Hash, (Get-Item -LiteralPath $taskPath).LastWriteTimeUtc.Ticks)
}
function Assert-Protected {
    foreach ($taskRelative in $taskProtected) {
        $taskPath = Join-Path $taskOutput $taskRelative
        Assert-Equal $taskSnapshot[$taskRelative][0] (Get-FileHash -LiteralPath $taskPath).Hash "数据内容被更改：$taskRelative"
        Assert-Equal $taskSnapshot[$taskRelative][1] (Get-Item -LiteralPath $taskPath).LastWriteTimeUtc.Ticks "数据文件被触碰：$taskRelative"
    }
}
& (Join-Path $taskRepository 'scripts/Update-Preview.ps1') -PublishedDirectory $taskSource -OutputDirectory $taskOutput
Assert-Equal 'NEW EXE' ([IO.File]::ReadAllText((Join-Path $taskOutput 'NexusExplorer.exe'))) 'EXE 未更新'
Assert-Equal 'NEW DLL' ([IO.File]::ReadAllText((Join-Path $taskOutput 'native.dll'))) '依赖未更新'
Assert-Equal 'NEW' ((Get-Content -Raw -LiteralPath (Join-Path $taskOutput 'release.json') | ConvertFrom-Json).version) '发布元数据未更新'
Assert-Equal (Get-FileHash -LiteralPath (Join-Path $taskRepository 'docs/SDK-INTEGRATION-LESSONS.md')).Hash `
    (Get-FileHash -LiteralPath (Join-Path $taskOutput 'docs/SDK-INTEGRATION-LESSONS.md')).Hash 'SDK 警示文档未随更新发布'
Assert-Protected
Write-Output 'PASS: 程序和依赖更新；默认及自定义 Storage、DB/WAL/SHM、配置、日志、界面状态内容和时间戳均保留。'

Write-Fixture 'published/NexusExplorer.exe' 'NEXT EXE'
$taskLocked = [IO.File]::Open((Join-Path $taskOutput 'native.dll'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try {
    $taskFailed = $false
    try { & (Join-Path $taskRepository 'scripts/Update-Preview.ps1') -PublishedDirectory $taskSource -OutputDirectory $taskOutput }
    catch { $taskFailed = $true }
    Assert-Equal $true $taskFailed '文件占用时未停止更新'
    Assert-Equal 'NEW EXE' ([IO.File]::ReadAllText((Join-Path $taskOutput 'NexusExplorer.exe'))) '占用失败前已替换 EXE'
    Assert-Protected
}
finally { $taskLocked.Dispose() }
Write-Output 'PASS: 程序文件占用时，在替换任何文件之前停止，数据和现有程序保留。'
