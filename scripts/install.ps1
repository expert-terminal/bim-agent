param([int]$RevitYear = 2026)
$ErrorActionPreference = 'Stop'
if ($RevitYear -ne 2026) { throw 'This prototype currently targets Revit 2026 only.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$assembly = Join-Path $root 'addin\bin\Release\net8.0-windows\BimAgent.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw "Build the add-in first: $assembly" }
$target = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
New-Item -ItemType Directory -Path $target -Force | Out-Null
$escaped = [System.Security.SecurityElement]::Escape($assembly)
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
$path = Join-Path $target 'BimAgent.addin'
[System.IO.File]::WriteAllText($path, $manifest)
Write-Host "Installed manifest: $path"
Write-Host 'Restart Revit to load the connector.'
