[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts/NexusExplorer-2.0.4-preview-win-x64',
    # Supply an existing publish directory to reuse a build or test the copy step.
    [string]$PublishedDirectory
)

$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
function Resolve-UpdatePath([string]$Value, [string]$Base) {
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path $Base $Value))
}
function Test-Within([string]$Value, [string]$Directory) {
    return $Value.Equals($Directory, [StringComparison]::OrdinalIgnoreCase) -or
        $Value.StartsWith($Directory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoJunction([string]$Value) {
    for ($taskAncestor = $Value; $taskAncestor; $taskAncestor = [IO.Path]::GetDirectoryName($taskAncestor)) {
        if ((Test-Path -LiteralPath $taskAncestor) -and
            ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "更新路径包含链接或联接点，停止更新：$taskAncestor"
        }
    }
}

$taskOutput = Resolve-UpdatePath $OutputDirectory $taskRepository
Assert-NoJunction $taskOutput
$taskExecutable = Join-Path $taskOutput 'NexusExplorer.exe'
foreach ($taskProcess in @(Get-Process -Name NexusExplorer -ErrorAction SilentlyContinue)) {
    if ($taskProcess.Path -and $taskProcess.Path.Equals($taskExecutable, [StringComparison]::OrdinalIgnoreCase)) {
        throw '请先关闭固定目录中的 NexusExplorer，再执行更新；不会自动结束程序或修改数据。'
    }
}

if (-not $PublishedDirectory) {
    $PublishedDirectory = 'artifacts/publish-staging'
    & dotnet publish (Join-Path $taskRepository 'src/NexusExplorer/NexusExplorer.csproj') -c Release -r win-x64 --self-contained true --no-restore -o (Join-Path $taskRepository $PublishedDirectory)
    if ($LASTEXITCODE -ne 0) { throw '发布失败，固定目录未修改。' }
    $taskProject = [xml](Get-Content -Raw -LiteralPath (Join-Path $taskRepository 'src/NexusExplorer/NexusExplorer.csproj'))
    $taskVersion = @($taskProject.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    $taskCommit = & git -C $taskRepository rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw '无法读取发布提交，固定目录未修改。' }
    @{ version = $taskVersion; commit = $taskCommit; architecture = 'win-x64'; updatedUtc = [DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRepository "$PublishedDirectory/release.json") -Encoding utf8
}
$taskSource = Resolve-UpdatePath $PublishedDirectory $taskRepository
Assert-NoJunction $taskSource
foreach ($taskRequired in @('NexusExplorer.exe', 'libvlc/win-x64/libvlc.dll', 'libvlc/win-x64/libvlccore.dll', 'libvlc/win-x64/plugins/plugins.dat')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskSource $taskRequired) -PathType Leaf)) {
        throw "发布文件不完整，固定目录未修改：$taskRequired"
    }
}

$taskProtectedRoots = @('Storage', 'data', 'logs') | ForEach-Object { Join-Path $taskOutput $_ }
$taskDatabase = Join-Path $taskOutput 'data/nexus.db'
$taskSettings = Join-Path $taskOutput 'appsettings.json'
if (Test-Path -LiteralPath $taskSettings) {
    $taskConfig = Get-Content -Raw -LiteralPath $taskSettings | ConvertFrom-Json
    if ($taskConfig.Database.Path) { $taskDatabase = Resolve-UpdatePath $taskConfig.Database.Path $taskOutput }
    if ($taskConfig.Storage.RootPath) { $taskProtectedRoots += Resolve-UpdatePath $taskConfig.Storage.RootPath $taskOutput }
    if ($taskConfig.Logging.Directory) { $taskProtectedRoots += Resolve-UpdatePath $taskConfig.Logging.Directory $taskOutput }
}
function Test-Protected([string]$Destination) {
    if ($Destination.Equals($taskSettings, [StringComparison]::OrdinalIgnoreCase) -or
        $Destination.StartsWith($taskDatabase, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    foreach ($taskRoot in $taskProtectedRoots) {
        if (Test-Within $Destination $taskRoot) { return $true }
    }
    return $false
}

$taskCopies = [Collections.Generic.List[object]]::new()
function Add-ProgramCopy([string]$Source, [string]$Relative) {
    $taskDestination = Resolve-UpdatePath $Relative $taskOutput
    if (-not (Test-Within $taskDestination $taskOutput)) { throw '程序目标路径越出固定目录，停止更新。' }
    if (Test-Protected $taskDestination) { return }
    Assert-NoJunction $Source
    Assert-NoJunction $taskDestination
    $taskCopies.Add([pscustomobject]@{ Source = $Source; Destination = $taskDestination })
}
# Never copy arbitrary root files or application data from a publish directory.
foreach ($taskFile in Get-ChildItem -LiteralPath $taskSource -File) {
    if ($taskFile.Name -eq 'NexusExplorer.exe' -or $taskFile.Extension -eq '.dll' -or
        $taskFile.Name -in @('NexusExplorer.deps.json', 'NexusExplorer.runtimeconfig.json', 'release.json')) {
        Add-ProgramCopy $taskFile.FullName $taskFile.Name
    }
}
foreach ($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskSource 'libvlc') -File -Recurse) {
    Add-ProgramCopy $taskFile.FullName ([IO.Path]::GetRelativePath($taskSource, $taskFile.FullName))
}
foreach ($taskDocument in @('docs/ACCEPTANCE.md', 'docs/VERIFICATION.md', 'docs/FEEDBACK-2.0.1.md', 'docs/ORGANIZATION-STATUS-2.0.2.md', 'docs/SDK-INTEGRATION-LESSONS.md', 'docs/AUDIO-TRACK-RESTORE.md', 'README.md', 'HANDOVER.md')) {
    $taskDocumentSource = Join-Path $taskRepository $taskDocument
    # Ignored local documents are optional in a fresh Git checkout.
    if (Test-Path -LiteralPath $taskDocumentSource -PathType Leaf) {
        Add-ProgramCopy $taskDocumentSource $taskDocument
    }
}
if (-not ($taskCopies | Where-Object { $_.Destination -eq $taskExecutable })) {
    throw '程序目标与受保护数据路径冲突，固定目录未修改。'
}
# Detect locked program files before replacing any file. Do not stop a user process.
foreach ($taskCopy in $taskCopies) {
    if (Test-Path -LiteralPath $taskCopy.Destination -PathType Leaf) {
        $taskLock = [IO.File]::Open($taskCopy.Destination, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $taskLock.Dispose()
    }
}
foreach ($taskCopy in $taskCopies) {
    if ($taskCopy.Source.Equals($taskCopy.Destination, [StringComparison]::OrdinalIgnoreCase)) { continue }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskCopy.Destination)) | Out-Null
    Copy-Item -LiteralPath $taskCopy.Source -Destination $taskCopy.Destination -Force
}
Write-Output "固定目录已更新：$taskOutput；Storage、数据库、配置及日志未复制、移动或清空。"
