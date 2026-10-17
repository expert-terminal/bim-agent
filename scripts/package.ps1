param(
    [string]$Version = '0.1.0-preview',
    [string]$AssemblyPath = ''
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $root 'addin\bin\Release\net8.0-windows\BimAgent.dll' }
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "BIM-agent-$Version"
$zip = "$stage.zip"
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
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Write-Host "Created $zip"
