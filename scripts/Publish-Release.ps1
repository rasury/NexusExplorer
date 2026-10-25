[CmdletBinding()]
param([switch]$ResumeDraft)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repo = 'rasury/NexusExplorer'
$oldToken = $env:GH_TOKEN
Push-Location $root
try {
    $dirty = & git status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Commit tracked changes before publishing.' }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read HEAD.' }
    $project = [xml](Get-Content 'src/NexusExplorer/NexusExplorer.csproj' -Raw)
    $version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if ($version -notmatch '^\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid project version.' }
    $tag = "v$version"
    $releaseTools = & "$PSScriptRoot/Restore-ReleaseTools.ps1"
    $gh = $releaseTools.Gh
    # Windows PowerShell treats redirected native stderr as an ErrorRecord.
    $ErrorActionPreference = 'Continue'
    & $gh auth status *> $null
    $authenticated = $LASTEXITCODE -eq 0
    $ErrorActionPreference = 'Stop'
    if (!$authenticated -and !$env:GH_TOKEN) {
        # Use the existing Git login only in this process; never print or persist its token.
        $credential = "protocol=https`nhost=github.com`n`n" | & git credential fill
        if ($LASTEXITCODE -ne 0) { throw 'Sign in with gh auth login or set GH_TOKEN.' }
        foreach ($line in $credential) { if ($line.StartsWith('password=')) { $env:GH_TOKEN = $line.Substring(9) } }
        $credential = $null
    }
    $ErrorActionPreference = 'Continue'
    & $gh auth status *> $null
    $authenticated = $LASTEXITCODE -eq 0
    $ErrorActionPreference = 'Stop'
    if (!$authenticated) { throw 'GitHub authentication unavailable.' }
    $remote = & $gh api "repos/$repo/commits/main" --jq '.sha'
    if ($LASTEXITCODE -ne 0 -or $remote.Trim() -ne $commit) { throw 'Push this exact commit to main before publishing.' }
    $releases = & $gh api "repos/$repo/releases?per_page=100"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read existing releases.' }
    $existing = @(($releases | ConvertFrom-Json) | Where-Object { $_.tag_name -eq $tag })
    if ($existing.Count -gt 1) { throw 'Multiple drafts share this tag. Resolve them before publishing.' }
    if ($existing.Count -and (!$ResumeDraft -or !$existing[0].draft)) { throw 'Version already exists. Use a new version, or -ResumeDraft for an unpublished release.' }
    $releaseCommit = if ($existing.Count) { $existing[0].target_commitish } else { $commit }
    # Also refuse an existing tag pointing to an older commit.
    $tagRef = & $gh api "repos/$repo/git/matching-refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify tag.' }
    $exact = @(($tagRef | ConvertFrom-Json) | Where-Object { $_.ref -eq "refs/tags/$tag" })
    if ($exact.Count -and $exact[0].object.sha -ne $releaseCommit) { throw 'Tag already points to another object.' }
    $output = Join-Path $root 'artifacts/releases'
    $assets = @("NexusExplorer-$version-win-x64-Portable.zip", "NexusExplorer-$version-win-x64-Setup.exe", 'SHA256SUMS.txt')
    if ($existing.Count -and (Test-Path -LiteralPath (Join-Path $output $assets[0]))) {
        # Resume the original build, even if only the publisher was fixed afterwards.
        # Never attach binaries from another commit to this draft.
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $output $assets[0]))
        try {
            $reader = [IO.StreamReader]::new($zip.GetEntry('release.json').Open())
            try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        } finally { $zip.Dispose() }
        if ($metadata.commit -ne $releaseCommit -or $metadata.version -ne $version) { throw 'Local assets do not match the draft commit/version.' }
        foreach ($name in $assets) { if (!(Test-Path -LiteralPath (Join-Path $output $name))) { throw "Missing resume asset: $name" } }
    } else {
        if ($releaseCommit -ne $commit) { throw 'Missing original draft assets. Build them from the draft commit first.' }
        & "$PSScriptRoot/Update-Preview.ps1"
        & "$PSScriptRoot/Build-Installer.ps1" -Iscc $releaseTools.Iscc
    }
    if (!$existing.Count) {
        $args = @('release', 'create', $tag, '--repo', $repo, '--target', $commit, '--title', "NexusExplorer $version", '--draft', '--notes-file', "$PSScriptRoot/Release-Notes.md")
        if ($version.Contains('-')) { $args += '--prerelease' }
        & $gh @args
        if ($LASTEXITCODE -ne 0) { throw 'Draft release creation failed.' }
    }
    foreach ($name in $assets) {
        $local = Get-Item -LiteralPath (Join-Path $output $name)
        $hash = (Get-FileHash -LiteralPath $local.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $alreadyUploaded = @($existing | ForEach-Object { $_.assets } | Where-Object { $_.name -eq $name -and $_.size -eq $local.Length -and $_.digest -eq "sha256:$hash" })
        if ($alreadyUploaded.Count -eq 1) { Write-Output "Verified existing upload: $name"; continue }
        Write-Output "Uploading: $name ($($local.Length) bytes)"
        & $gh release upload $tag (Join-Path $output $name) --repo $repo --clobber
        if ($LASTEXITCODE -ne 0) { throw 'Upload failed; release remains a draft. Retry with -ResumeDraft.' }
    }
    # Draft releases may have no public tag yet: tags/{tag} returns 404.
    $drafts = & $gh api "repos/$repo/releases?per_page=100"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate draft for verification.' }
    $draft = @(($drafts | ConvertFrom-Json) | Where-Object { $_.tag_name -eq $tag })
    if ($draft.Count -ne 1) { throw 'Draft release is missing or ambiguous.' }
    $publishedAssets = & $gh api "repos/$repo/releases/$($draft[0].id)"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify uploaded assets.' }
    $release = $publishedAssets | ConvertFrom-Json
    if (!$release.draft -or $release.target_commitish -ne $releaseCommit -or $release.tag_name -ne $tag) { throw 'Release identity changed during upload.' }
    foreach ($name in $assets) {
        $local = Get-Item -LiteralPath (Join-Path $output $name)
        $asset = @($release.assets | Where-Object name -eq $name)
        if ($asset.Count -ne 1 -or $asset[0].size -ne $local.Length) { throw "Uploaded asset size mismatch: $name" }
        $hash = (Get-FileHash -LiteralPath $local.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($asset[0].digest -and $asset[0].digest -ne "sha256:$hash") { throw "Uploaded asset checksum mismatch: $name" }
    }
    & $gh release edit $tag --repo $repo --draft=false
    if ($LASTEXITCODE -ne 0) { throw 'Publishing failed; assets remain in draft.' }
    Write-Output "Published: https://github.com/$repo/releases/tag/$tag"
}
finally { $env:GH_TOKEN = $oldToken; Pop-Location }
