[CmdletBinding()]
param(
    [string]$ArtifactsDirectory = 'artifacts',
    [string]$PublishedDirectory,
    [string]$PythonExecutable
)
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
function Resolve-ReleasePath([string]$Value) {
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path $taskRepository $Value))
}
function Assert-ReleasePath([string]$Value) {
    for ($taskAncestor = $Value; $taskAncestor; $taskAncestor = [IO.Path]::GetDirectoryName($taskAncestor)) {
        if ((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "发布路径包含链接或联接点，停止发布：$taskAncestor"
        }
    }
}
if (-not $PythonExecutable) {
    $taskBundledPython = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
    if (Test-Path -LiteralPath $taskBundledPython) { $PythonExecutable = $taskBundledPython }
    else {
        foreach ($taskName in @('python', 'python3', 'py')) {
            $taskCommand = Get-Command $taskName -CommandType Application -ErrorAction SilentlyContinue
            if ($taskCommand -and $taskCommand.Source -notlike '*\Microsoft\WindowsApps\*') { $PythonExecutable = $taskCommand.Source; break }
        }
    }
}
if (-not $PythonExecutable) { throw '打包需要 Python 3，请通过 -PythonExecutable 指定其路径。' }
$taskArtifacts = Resolve-ReleasePath $ArtifactsDirectory
Assert-ReleasePath $taskArtifacts
[IO.Directory]::CreateDirectory($taskArtifacts) | Out-Null
$taskArchive = Join-Path $taskArtifacts 'NexusExplorer_new.zip'
$taskOutput = Join-Path $taskArtifacts 'NexusExplorer_new'
Assert-ReleasePath $taskArchive; Assert-ReleasePath $taskOutput
foreach ($taskProcess in @(Get-Process -Name NexusExplorer -ErrorAction SilentlyContinue)) {
    if ($taskProcess.Path -and $taskProcess.Path.Equals((Join-Path $taskOutput 'NexusExplorer.exe'), [StringComparison]::OrdinalIgnoreCase)) {
        throw '请先关闭 NexusExplorer_new 目录中的程序，再执行发布；不会自动结束进程。'
    }
}
$taskLockPath = Join-Path $taskArtifacts '.publish.lock'
Assert-ReleasePath $taskLockPath
$taskLock = [IO.FileStream]::new($taskLockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite,
    [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
$taskWork = Join-Path $taskArtifacts ('.release-work-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($taskWork) | Out-Null
    if (-not $PublishedDirectory) {
        $PublishedDirectory = Join-Path $taskWork 'publish'
        & dotnet restore (Join-Path $taskRepository 'src/NexusExplorer/NexusExplorer.csproj') -r win-x64 -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw '依赖还原失败，现有便携包未修改。' }
        & dotnet publish (Join-Path $taskRepository 'src/NexusExplorer/NexusExplorer.csproj') -c Release -r win-x64 --self-contained true --no-restore -o $PublishedDirectory -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw '发布构建失败，现有便携包未修改。' }
    }
    $taskPublished = Resolve-ReleasePath $PublishedDirectory
    Assert-ReleasePath $taskPublished
    $taskCommit = & git -C $taskRepository rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw '无法读取当前提交，停止发布。' }
    $taskProject = [xml](Get-Content -Raw -LiteralPath (Join-Path $taskRepository 'src/NexusExplorer/NexusExplorer.csproj'))
    $taskNative = Get-Content -Raw -LiteralPath (Join-Path $taskPublished 'native/mpv/win-x64/runtime-manifest.json') | ConvertFrom-Json
    @{ version = @($taskProject.Project.PropertyGroup.Version | Where-Object { $_ })[0]; commit = $taskCommit;
       architecture = 'win-x64'; engine = 'libmpv'; nativeVersion = $taskNative.version; nativeApi = $taskNative.apiVersion;
       updatedUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json |
       Set-Content -LiteralPath (Join-Path $taskPublished 'release.json') -Encoding utf8
    $taskSourceZip = Join-Path $taskPublished 'licenses/NexusExplorer-source.zip'
    [IO.Directory]::CreateDirectory((Split-Path -Parent $taskSourceZip)) | Out-Null
    & git -C $taskRepository archive --format=zip "--output=$taskSourceZip" HEAD
    if ($LASTEXITCODE -ne 0) { throw '对应源码打包失败，停止发布。' }

    # Replace the archive first; update the runtime from its verified extraction.
    $taskExtracted = Join-Path $taskWork 'extracted'
    & $PythonExecutable (Join-Path $PSScriptRoot 'package_portable.py') --published-directory $taskPublished --archive $taskArchive --extract-directory $taskExtracted
    if ($LASTEXITCODE -ne 0) { throw '便携包或解压校验失败，运行目录未修改。' }
    & (Join-Path $PSScriptRoot 'validate_fixed_update.ps1') -PublishedDirectory $taskExtracted -OutputDirectory $taskOutput
    Write-Output "发布完成：$taskArchive；解压运行目录：$taskOutput"
}
finally {
    # Delete only this invocation's disposable workspace, never the runtime directory.
    try {
        $taskWorkFull = [IO.Path]::GetFullPath($taskWork)
        $taskPrefix = $taskArtifacts.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        if (-not $taskWorkFull.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($taskWorkFull) -notmatch '^\.release-work-[0-9a-f]{32}$') { throw '临时目录边界检查失败，停止清理。' }
        Assert-ReleasePath $taskWorkFull
        if (Test-Path -LiteralPath $taskWorkFull) { Remove-Item -LiteralPath $taskWorkFull -Recurse -Force }
    }
    finally {
        $taskLock.Dispose()
    }
}
