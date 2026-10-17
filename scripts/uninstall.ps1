param([int]$RevitYear = 2026)
$path = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear\BimAgent.addin"
if (Test-Path -LiteralPath $path) {
    Remove-Item -LiteralPath $path
    Write-Host "Removed manifest: $path"
} else {
    Write-Host 'BIM-agent manifest was not installed.'
}
Write-Host 'Restart Revit to unload the connector.'
