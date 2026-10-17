param([string]$Version = '0.3.0-preview')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stage = Join-Path $root 'dist\sketchup-rbz'
New-Item -ItemType Directory -Path (Join-Path $stage 'bim_agent') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'sketchup\bim_agent.rb') -Destination (Join-Path $stage 'bim_agent.rb') -Force
Copy-Item -LiteralPath (Join-Path $root 'sketchup\bim_agent\main.rb') -Destination (Join-Path $stage 'bim_agent\main.rb') -Force
$zip = Join-Path $root "dist\BIM-agent-SketchUp-$Version.zip"
$rbz = Join-Path $root "dist\BIM-agent-SketchUp-$Version.rbz"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Move-Item -LiteralPath $zip -Destination $rbz -Force
Write-Host "Created $rbz"
