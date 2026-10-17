param([int]$RevitYear = 2026)
$ErrorActionPreference = 'Stop'
if ($RevitYear -ne 2026) { throw 'This preview supports Revit 2026 only.' }
$source = (Resolve-Path $PSScriptRoot).Path
$assembly = Join-Path $source 'BimAgent.dll'
$server = Join-Path $source 'server.mjs'
if (-not (Test-Path -LiteralPath $assembly) -or -not (Test-Path -LiteralPath $server)) {
    throw 'Extract the complete BIM-agent release ZIP before running install.ps1.'
}
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw 'Node.js 22 or newer is required for the MCP server. Install Node.js, then rerun this script.'
}
$nodeVersion = [version]((& node --version).TrimStart('v'))
if ($nodeVersion.Major -lt 22) { throw "Node.js 22 or newer is required; found $nodeVersion." }
$version = (Get-Item -LiteralPath $assembly).VersionInfo.FileVersion
if (-not $version) { $version = 'preview' }
$target = Join-Path $env:LOCALAPPDATA "BIM-agent\$version"
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -LiteralPath $assembly -Destination (Join-Path $target 'BimAgent.dll') -Force
Copy-Item -LiteralPath $server -Destination (Join-Path $target 'server.mjs') -Force
$addinDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
New-Item -ItemType Directory -Path $addinDir -Force | Out-Null
$escaped = [System.Security.SecurityElement]::Escape((Join-Path $target 'BimAgent.dll'))
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>BIM-agent</Name>
    <Assembly>$escaped</Assembly>
    <AddInId>2B971B4B-8B38-4C96-9A61-6CBF2D276092</AddInId>
    <FullClassName>BimAgent.BridgeApp</FullClassName>
    <VendorId>RAB1</VendorId>
    <VendorDescription>Open-source BIM-agent</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
[System.IO.File]::WriteAllText((Join-Path $addinDir 'BimAgent.addin'), $manifest)
Write-Host "Installed BIM-agent to $target"
Write-Host "Agent MCP server: $(Join-Path $target 'server.mjs')"
Write-Host 'Restart Revit to load the add-in. Configure your MCP client with node and the server path above.'
