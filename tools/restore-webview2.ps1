$ErrorActionPreference = 'Stop'
$version = '1.0.4258.31'
$expected = '56F7F4B8BF9AEE4B8EFEFBBDD4F67D5F74EBD1B100ED0806DA71BF76AF481AA9'
$cache = Join-Path (Split-Path $PSScriptRoot -Parent) '.packages\webview2'
$sdk = Join-Path $cache $version
if (Test-Path -LiteralPath (Join-Path $sdk 'lib\net462\Microsoft.Web.WebView2.Wpf.dll')) { return }
$null = New-Item -ItemType Directory -Force -Path $cache
$package = Join-Path $cache ('microsoft.web.webview2.' + $version + '.nupkg')
if (!(Test-Path -LiteralPath $package)) {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri ('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/' + $version + '/microsoft.web.webview2.' + $version + '.nupkg') -OutFile $package
}
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected) { throw 'WebView2 package checksum mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($package, $sdk)
