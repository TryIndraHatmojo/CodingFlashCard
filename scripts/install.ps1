param([switch]$Launch)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$source = Join-Path $projectRoot 'artifacts\publish'
if (-not (Test-Path -LiteralPath (Join-Path $source 'CodingFlashcards.exe'))) { & (Join-Path $PSScriptRoot 'build.ps1') -Publish }
$destination = Join-Path $env:LOCALAPPDATA 'Programs\CodingFlashcards'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Coding Flashcards.lnk'))
$shortcut.TargetPath = Join-Path $destination 'CodingFlashcards.exe'
$shortcut.WorkingDirectory = $destination
$shortcut.Save()
Write-Host "Installed: $destination"
if ($Launch) { Start-Process -FilePath (Join-Path $destination 'CodingFlashcards.exe') -WindowStyle Hidden }
