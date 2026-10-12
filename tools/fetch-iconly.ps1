param([string]$Destination = (Join-Path $PSScriptRoot '..\assets\iconly'), [string]$Query, [int]$TypeId = 1, [int]$InspectId, [string]$Asset, [switch]$Audit, [string]$AuditReport = (Join-Path $PSScriptRoot '..\artifacts\iconly-audit.json'))
$ErrorActionPreference = 'Stop'
if (-not $env:ICONLY_API_KEY) { throw 'Set ICONLY_API_KEY for this process before running this script.' }
$headers = @{ Authorization = ('Bearer ' + $env:ICONLY_API_KEY); Accept = 'application/json, text/event-stream' }
$endpoint = 'https://mcp.iconly.pro/mcp'
function Invoke-Rpc($method, $parameters, $id = 1) {
    $body = @{ jsonrpc = '2.0'; method = $method; params = $parameters; id = $id } | ConvertTo-Json -Depth 12 -Compress
    $response = Invoke-WebRequest -Uri $endpoint -Method Post -Headers $headers -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 30
    if ($response.Headers['Mcp-Session-Id']) { $headers['Mcp-Session-Id'] = $response.Headers['Mcp-Session-Id'] }
    $line = @($response.Content -split "`n" | Where-Object { $_ -match '^data: ' })[-1]
    if ($line) { $rpc = $line.Substring(6) | ConvertFrom-Json } else { $rpc = $response.Content | ConvertFrom-Json }
    if ($rpc.error) { throw $rpc.error.message }
    return $rpc.result
}
function Invoke-Iconly($name, $arguments) {
    $result = Invoke-Rpc 'tools/call' @{ name = $name; arguments = $arguments }
    if ($result.isError) { throw ($result.content[0].text) }
    return ($result.content | Where-Object type -eq 'text' | Select-Object -First 1).text | ConvertFrom-Json
}
$null = Invoke-Rpc 'initialize' @{ protocolVersion = '2025-03-26'; capabilities = @{}; clientInfo = @{ name = 'dynamic-island'; version = '1.0.0' } }
$headers['MCP-Protocol-Version'] = '2025-03-26'
$null = Invoke-WebRequest -Uri $endpoint -Method Post -Headers $headers -ContentType 'application/json' -Body '{"jsonrpc":"2.0","method":"notifications/initialized"}' -UseBasicParsing
$null = New-Item -ItemType Directory -Force -Path $Destination
if ($InspectId) { Invoke-Iconly 'fetch_icon_svg' @{ id = $InspectId } | ConvertTo-Json -Depth 8; return }
if ($Audit) {
    $manifest = Get-Content -LiteralPath (Join-Path $Destination 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $report = @()
    foreach ($iconAsset in $manifest) {
        $original = Invoke-Iconly 'fetch_icon_svg' @{ id = $iconAsset.id }
        $local = [IO.File]::ReadAllText((Join-Path $Destination $iconAsset.file))
        $same = $local.Replace("`r`n", "`n").Trim() -ceq $original.content.Replace("`r`n", "`n").Trim()
        $report += @{ file = $iconAsset.file; id = $iconAsset.id; name = $original.name; matchesOriginal = $same; style = $iconAsset.style; type = $iconAsset.type }
        if (-not $same) { throw ('SVG differs from Iconly original: ' + $iconAsset.file) }
        Write-Output ('Verified original: ' + $iconAsset.file + ' / ' + $iconAsset.id)
    }
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $AuditReport)
    [IO.File]::WriteAllText($AuditReport, ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    return
}
if ($Query) {
    $result = Invoke-Iconly 'search_icons' @{ q = $Query; style_id = 4; page_size = 30 }
    $result.icons | Select-Object id,name,type_id | ConvertTo-Json -Compress
    return
}
$names = [ordered]@{ setting = 'Setting'; folder = 'Folder'; document = 'Document'; calculator = 'Calculator Casino'; home = 'Home'; time = 'Time Circle'; play = 'Play'; pause = 'Pause'; close = 'Close Square'; pin = 'Bookmark'; stopwatch = 'Time Square'; brand = 'Category'; edit = 'Edit'; 'arrow-up' = 'Arrow Up MD'; 'arrow-down' = 'Arrow Down MD' }
$manifest = @()
if ($Asset) {
    if (-not $names.Contains($Asset)) { throw 'Unknown asset name.' }
    $existing = Get-Content -LiteralPath (Join-Path $Destination 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($item in $existing) { if ($item.file -ne ($Asset + '.svg')) { $manifest += $item } }
}
foreach ($entry in $names.GetEnumerator()) {
    if ($Asset -and $entry.Key -ne $Asset) { continue }
    $found = Invoke-Iconly 'search_icons' @{ q = $entry.Value; style_id = 4; type_id = $TypeId; page_size = 50 }
    $icon = $found.icons | Where-Object { $_.name.Trim() -eq $entry.Value } | Sort-Object id | Select-Object -First 1
    if (-not $icon -or -not $icon.svg_url) { throw ('Unavailable: ' + $entry.Key) }
    $response = Invoke-WebRequest -Uri $icon.svg_url -UseBasicParsing -TimeoutSec 30
    [IO.File]::WriteAllText((Join-Path $Destination ($entry.Key + '.svg')), $response.Content, [Text.UTF8Encoding]::new($false))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes((Join-Path $Destination ($entry.Key + '.svg'))))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
    $manifest += @{ file = $entry.Key + '.svg'; id = $icon.id; name = $icon.name; style = $icon.style.name; type = $icon.icon_type.name; source = 'https://iconly.pro'; provider = 'Iconly Pro'; sha256 = $digest }
    Write-Output ('Downloaded ' + $entry.Key + ': ' + $icon.id + ' / ' + $icon.name)
}
[IO.File]::WriteAllText((Join-Path $Destination 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
