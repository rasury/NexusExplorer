param([string]$PythonExecutable)
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskFixture = Join-Path $taskRepository ('artifacts/.publish-test-' + [Guid]::NewGuid().ToString('N'))
$taskSource = Join-Path $taskFixture 'published'
$taskArtifacts = Join-Path $taskFixture 'artifacts'
function Write-Fixture([string]$Name, [string]$Content) {
    $taskPath = Join-Path $taskSource $Name
    [IO.Directory]::CreateDirectory((Split-Path -Parent $taskPath)) | Out-Null
    [IO.File]::WriteAllText($taskPath, $Content)
}
function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw $Message }
}
try {
    foreach ($taskName in @('NexusExplorer.exe','e_sqlite3.dll','PresentationNative_cor3.dll','wpfgfx_cor3.dll',
        'native/mpv/win-x64/libmpv-2.dll','native/mpv/input.conf','licenses/NexusExplorer-GPL-2.0.txt','licenses/SOURCE.txt')) { Write-Fixture $taskName $taskName }
    Write-Fixture 'appsettings.json' '{"Storage":{"RootPath":""},"Database":{"Path":""},"Logging":{"Directory":""}}'
    Write-Fixture 'native/mpv/win-x64/runtime-manifest.json' (@{ version='test'; apiVersion='2.5'; dllSha256=(Get-FileHash -LiteralPath (Join-Path $taskSource 'native/mpv/win-x64/libmpv-2.dll')).Hash.ToLowerInvariant() } | ConvertTo-Json)
    $taskArguments = @{ PublishedDirectory=$taskSource; ArtifactsDirectory=$taskArtifacts }
    if ($PythonExecutable) { $taskArguments.PythonExecutable=$PythonExecutable }
    & (Join-Path $taskRepository 'scripts/Update-Preview.ps1') @taskArguments
    $taskRuntime = Join-Path $taskArtifacts 'NexusExplorer_new'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $taskArtifacts 'NexusExplorer_new.zip')) '缺少固定名称 ZIP'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $taskRuntime 'NexusExplorer.exe')) '解压目录套了多余层级'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $taskRuntime 'appsettings.json')) '首次解压缺少默认配置'
    [IO.Directory]::CreateDirectory((Join-Path $taskRuntime 'Storage')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $taskRuntime 'data')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskRuntime 'Storage/keep.txt'), 'USER STORAGE')
    [IO.File]::WriteAllText((Join-Path $taskRuntime 'data/nexus.db'), 'USER DB')
    $taskSettings = Join-Path $taskRuntime 'appsettings.json'
    [IO.File]::WriteAllText($taskSettings, '{"Storage":{"RootPath":""},"Database":{"Path":""},"Logging":{"Directory":""},"Keep":"USER CONFIG"}')
    Write-Fixture 'NexusExplorer.exe' 'NEXT EXE'
    & (Join-Path $taskRepository 'scripts/Update-Preview.ps1') @taskArguments
    Assert-Equal 'NEXT EXE' ([IO.File]::ReadAllText((Join-Path $taskRuntime 'NexusExplorer.exe'))) '第二次没有更新程序'
    Assert-Equal 'USER STORAGE' ([IO.File]::ReadAllText((Join-Path $taskRuntime 'Storage/keep.txt'))) 'Storage 被改动'
    Assert-Equal 'USER DB' ([IO.File]::ReadAllText((Join-Path $taskRuntime 'data/nexus.db'))) '数据库被改动'
    Assert-Equal 'USER CONFIG' ((Get-Content -LiteralPath $taskSettings -Raw | ConvertFrom-Json).Keep) '配置被覆盖'
    Assert-Equal 2 @(Get-ChildItem -LiteralPath $taskArtifacts -Force).Count '发布留下了中间副本或历史版本目录'
    Write-Output 'PASS: 首次和重复发布固定 ZIP/目录，程序更新且数据配置保留，中间副本清理。'
}
finally {
    $taskFull = [IO.Path]::GetFullPath($taskFixture)
    $taskParent = [IO.Path]::GetFullPath((Join-Path $taskRepository 'artifacts')).TrimEnd('\') + '\'
    if (-not $taskFull.StartsWith($taskParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($taskFull) -notmatch '^\.publish-test-[0-9a-f]{32}$') { throw '测试目录越出边界，停止清理。' }
    if (Test-Path -LiteralPath $taskFull) { Remove-Item -LiteralPath $taskFull -Recurse -Force }
}
