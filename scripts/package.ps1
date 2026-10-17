param(
    [string]$Version = '0.3.0-preview',
    [string]$AssemblyPath = ''
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $root 'addin\bin\Release\net8.0-windows\BimAgent.dll' }
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "BIM-agent-$Version"
$zip = "$stage.zip"
New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (Test-Path -LiteralPath $stage) {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    $resolvedDist = (Resolve-Path -LiteralPath $dist).Path.TrimEnd('\') + '\'
    if (-not $resolvedStage.StartsWith($resolvedDist, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear package staging directory outside dist: $resolvedStage"
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath $assembly -Destination (Join-Path $stage 'BimAgent.dll') -Force
Copy-Item -LiteralPath (Join-Path $root 'mcp\server.mjs') -Destination (Join-Path $stage 'server.mjs') -Force
Copy-Item -LiteralPath (Join-Path $root 'scripts\install-binary.ps1') -Destination (Join-Path $stage 'install.ps1') -Force
Copy-Item -LiteralPath (Join-Path $root 'scripts\uninstall.ps1') -Destination (Join-Path $stage 'uninstall.ps1') -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $stage 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $stage 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'SECURITY.md') -Destination (Join-Path $stage 'SECURITY.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'CHANGELOG.md') -Destination (Join-Path $stage 'CHANGELOG.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'STUDY.md') -Destination (Join-Path $stage 'STUDY.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'CONTRIBUTING.md') -Destination (Join-Path $stage 'CONTRIBUTING.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination (Join-Path $stage 'docs') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'AGENTS.md') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root 'capabilities.json') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root 'mcp') -Destination $stage -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $stage 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'scripts\bim-agent-cli.mjs') -Destination (Join-Path $stage 'scripts') -Force
Copy-Item -LiteralPath (Join-Path $root 'scripts\install-autocad.ps1') -Destination (Join-Path $stage 'scripts') -Force
Copy-Item -LiteralPath (Join-Path $root 'scripts\uninstall-autocad.ps1') -Destination (Join-Path $stage 'scripts') -Force
$autocadSource = Join-Path $stage 'autocad'
New-Item -ItemType Directory -Path $autocadSource -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'autocad\BimAgent.AutoCAD.csproj') -Destination $autocadSource -Force
Copy-Item -LiteralPath (Join-Path $root 'autocad\BridgeApp.cs') -Destination $autocadSource -Force
$autocadDll = Join-Path $root 'autocad\bin\Release\net8.0-windows\BimAgent.AutoCAD.dll'
if (Test-Path -LiteralPath $autocadDll) {
    $autocadOutput = Join-Path $stage 'autocad\bin\Release\net8.0-windows'
    New-Item -ItemType Directory -Path $autocadOutput -Force | Out-Null
    Copy-Item -LiteralPath $autocadDll -Destination $autocadOutput -Force
}
$sketchupPackage = Join-Path $dist "BIM-agent-SketchUp-$Version.rbz"
& (Join-Path $root 'scripts\package-sketchup.ps1') -Version $Version
Copy-Item -LiteralPath $sketchupPackage -Destination (Join-Path $stage 'BIM-agent-SketchUp.rbz') -Force
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Write-Host "Created $zip"
