param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { python (Join-Path $PSScriptRoot 'bootstrap.py'); if ($LASTEXITCODE) { throw 'SDK bootstrap failed' } }
$env:DOTNET_ROOT = Split-Path $sdk
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $projectRoot 'src\CodingFlashcards\CodingFlashcards.csproj'
if ($Publish) {
 python (Join-Path $PSScriptRoot 'runtime_feed.py'); if ($LASTEXITCODE) { throw 'Runtime download failed' }
 & $sdk publish $project -c Release -r win-x64 --self-contained true --source (Join-Path $projectRoot '.tools\feed') -p:NuGetAudit=false -o (Join-Path $projectRoot 'artifacts\publish')
} else { & $sdk build $project -c Release }
if ($LASTEXITCODE) { throw 'Build failed' }
