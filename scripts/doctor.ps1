$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$checks = @(
    @{ App='Revit 2026'; Path='C:\Program Files\Autodesk\Revit 2026\Revit.exe'; Connector=(Join-Path $root 'addin\bin\Release\net8.0-windows\BimAgent.dll') },
    @{ App='AutoCAD 2026'; Path='C:\Program Files\Autodesk\AutoCAD 2026\acad.exe'; Connector=(Join-Path $root 'autocad\bin\Release\net8.0-windows\BimAgent.AutoCAD.dll') },
    @{ App='SketchUp'; Path='C:\Program Files\SketchUp'; Connector=(Join-Path $root 'sketchup\bim_agent.rb') },
    @{ App='Blender'; Path='C:\Program Files\Blender Foundation'; Connector='' },
    @{ App='3ds Max'; Path='C:\Program Files\Autodesk\3ds Max 2026'; Connector='' },
    @{ App='Maya'; Path='C:\Program Files\Autodesk\Maya2026'; Connector='' },
    @{ App='Lumion'; Path='C:\Program Files\Lumion 2023'; Connector='' },
    @{ App='Enscape'; Path='C:\Program Files\Enscape'; Connector='' }
)
$checks | ForEach-Object {
    [pscustomobject]@{
        Application = $_.App
        Installed = Test-Path -LiteralPath $_.Path
        ConnectorSourceOrBuild = if ($_.Connector) { Test-Path -LiteralPath $_.Connector } else { $false }
    }
} | Format-Table -AutoSize
$node = Get-Command node -ErrorAction SilentlyContinue
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
Write-Host "Node.js: $(if($node){& node --version}else{'not found'})"
Write-Host ".NET SDK: $(if($dotnet){(& dotnet --version)}else{'not found'})"
Write-Host 'Read capabilities.json for tested status. Installed software does not imply a working connector.'
