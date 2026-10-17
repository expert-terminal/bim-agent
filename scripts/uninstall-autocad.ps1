$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\BIM-agent-AutoCAD.bundle'
$resolvedRoot = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'))
$resolvedBundle = [IO.Path]::GetFullPath($bundle)
if (-not $resolvedBundle.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Bundle path is outside the AutoCAD per-user ApplicationPlugins folder.'
}
if (Test-Path -LiteralPath $resolvedBundle) { Remove-Item -LiteralPath $resolvedBundle -Recurse -Force }
Write-Host 'BIM-agent AutoCAD bundle removed. Restart AutoCAD.'
