param([switch]$Run, [string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 is required.' }
$destination = Join-Path $PSScriptRoot $OutputDirectory
$null = New-Item -ItemType Directory -Force -Path $destination
& (Join-Path $PSScriptRoot 'tools\restore-webview2.ps1')
& (Join-Path $PSScriptRoot 'tools\restore-media-sdk.ps1')
$webview = Join-Path $PSScriptRoot '.packages\webview2\1.0.4258.31'
$arguments = @('/nologo', '/target:winexe', '/optimize+', '/platform:x64', '/utf8output', '/codepage:65001', ('/out:' + (Join-Path $destination 'DynamicIsland.exe')), ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')))
foreach ($name in @('PresentationCore', 'PresentationFramework', 'WindowsBase')) { $arguments += '/reference:' + (Join-Path $framework ('WPF\' + $name + '.dll')) }
foreach ($name in @('System.Xaml', 'System.Windows.Forms', 'System.Drawing', 'System.Xml.Linq', 'System.Web.Extensions')) { $arguments += '/reference:' + (Join-Path $framework ($name + '.dll')) }
foreach ($name in @('Core', 'Wpf')) { $arguments += '/reference:' + (Join-Path $webview ('lib\net462\Microsoft.Web.WebView2.' + $name + '.dll')) }
$arguments += '/reference:' + (Join-Path $framework 'System.Runtime.WindowsRuntime.dll')
foreach ($name in @('System.Runtime', 'System.Threading.Tasks', 'System.Runtime.InteropServices.WindowsRuntime', 'System.ObjectModel')) {
    $facade = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR ('Microsoft.NET\assembly\GAC_MSIL\' + $name)) -Filter '*.dll' -Recurse | Select-Object -First 1
    $arguments += '/reference:' + $facade.FullName
}
foreach ($metadata in (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '.packages\windows-contracts\10.0.19041.1\ref\netstandard2.0') -Filter '*.winmd')) { $arguments += '/reference:' + $metadata.FullName }
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'src\Panel.xaml') + ',Panel.xaml'
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets\iconly') -Filter '*.svg' | ForEach-Object { $arguments += '/resource:' + $_.FullName + ',icons.' + $_.BaseName }
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\iconly\manifest.json') + ',icons.manifest'
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src\widgets') -Filter '*.xaml' -Recurse | ForEach-Object { $arguments += '/resource:' + $_.FullName + ',widgets.' + $_.Directory.Name + '.' + $_.Name }
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -Recurse | ForEach-Object { $arguments += $_.FullName }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\DynamicIsland.exe.config') -Destination $destination
foreach ($name in @('Core', 'Wpf')) { Copy-Item -LiteralPath (Join-Path $webview ('lib\net462\Microsoft.Web.WebView2.' + $name + '.dll')) -Destination $destination }
Copy-Item -LiteralPath (Join-Path $webview 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $destination
Copy-Item -LiteralPath (Join-Path $webview 'LICENSE.txt') -Destination (Join-Path $destination 'WebView2-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $webview 'NOTICE.txt') -Destination (Join-Path $destination 'WebView2-NOTICE.txt')
Write-Output ('Built: ' + (Join-Path $destination 'DynamicIsland.exe'))
if ($Run) { Start-Process -FilePath (Join-Path $destination 'DynamicIsland.exe') -WindowStyle Hidden }
