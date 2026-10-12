$ErrorActionPreference = 'Stop'
$version = '10.0.19041.1'
$expected = '7DC674587605F02B0B22266071834DD6E23EFAEED0D615C2A7F1C2601C18698D'
$cache = Join-Path (Split-Path $PSScriptRoot -Parent) '.packages\windows-contracts'
$sdk = Join-Path $cache $version
if (Test-Path -LiteralPath (Join-Path $sdk 'ref\netstandard2.0\Windows.Foundation.UniversalApiContract.winmd')) { return }
$null = New-Item -ItemType Directory -Force -Path $cache
$package = Join-Path $cache ($version + '.nupkg')
if (!(Test-Path -LiteralPath $package)) {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri ('https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.contracts/' + $version + '/microsoft.windows.sdk.contracts.' + $version + '.nupkg') -OutFile $package
}
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected) { throw 'Windows SDK Contracts checksum mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($package, $sdk)
