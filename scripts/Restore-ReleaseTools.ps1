[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$cache = Join-Path (Split-Path $PSScriptRoot -Parent) 'native/release-tools'
[IO.Directory]::CreateDirectory($cache) | Out-Null
function Download-Verified($Name, $Url, $Hash) {
    $path = Join-Path $cache $Name
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Hash) {
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Hash) { throw "Tool checksum mismatch: $Name" }
    return $path
}
$gh = Join-Path $cache 'gh/bin/gh.exe'
if (!(Test-Path -LiteralPath $gh)) {
    $zip = Download-Verified 'gh.zip' 'https://github.com/cli/cli/releases/download/v2.103.2/gh_2.103.2_windows_amd64.zip' 'fef9fcdd8fdabd0501ab8b3d99eac976f504ab55d01bef9e8ef428a5d900ddda'
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $cache 'gh') -Force
}
$iscc = Join-Path $cache 'inno/ISCC.exe'
if (!(Test-Path -LiteralPath $iscc)) {
    $setup = Download-Verified 'innosetup.exe' 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
    $target = Join-Path $cache 'inno'
    $process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS', '/TASKS=""', "/DIR=`"$target`"") -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw 'Inno Setup installation failed.' }
}
if (!(Test-Path -LiteralPath $gh) -or !(Test-Path -LiteralPath $iscc)) { throw 'Release tools missing.' }
[pscustomobject]@{ Gh = $gh; Iscc = $iscc }
