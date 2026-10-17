$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$assembly = Join-Path $root 'autocad\bin\Release\net8.0-windows\BimAgent.AutoCAD.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw 'Build autocad/BimAgent.AutoCAD.csproj in Release first.' }
$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\BIM-agent-AutoCAD.bundle'
$contents = Join-Path $bundle 'Contents'
New-Item -ItemType Directory -Path $contents -Force | Out-Null
Copy-Item -LiteralPath $assembly -Destination (Join-Path $contents 'BimAgent.AutoCAD.dll') -Force
$package = @'
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" ProductType="Application" AutodeskProduct="AutoCAD" Name="BIM-agent AutoCAD" AppVersion="0.4.0" Author="BIM-agent contributors">
  <Components>
    <RuntimeRequirements OS="Win64" Platform="AutoCAD" SeriesMin="R25.1" SeriesMax="R25.1" />
    <ComponentEntry AppName="BIM-agent AutoCAD" Version="0.4.0" ModuleName="./Contents/BimAgent.AutoCAD.dll" LoadOnAutoCADStartup="True" />
  </Components>
</ApplicationPackage>
'@
[System.IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'), $package)
Write-Host "Installed AutoCAD bundle: $bundle"
Write-Host 'Restart AutoCAD 2026 to load it.'
