# Installs Bayan colorization into Visual Studio 2022 (TextMate grammar drop-in).
# Restart Visual Studio afterwards. No VSIX needed.
$dest = Join-Path $env:USERPROFILE ".vs\Extensions\bayan"
$src  = Join-Path $PSScriptRoot "..\vscode"
New-Item -ItemType Directory -Force -Path (Join-Path $dest "Syntaxes") | Out-Null
Copy-Item (Join-Path $src "syntaxes\bayan.tmLanguage.json") (Join-Path $dest "Syntaxes\bayan.tmLanguage.json") -Force
Copy-Item (Join-Path $src "language-configuration.json") (Join-Path $dest "language-configuration.json") -Force
Write-Host "Installed to $dest. Restart Visual Studio."
