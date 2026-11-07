# Cleanup after spec 011, the self-hosted C emitter (2026-11-04), on the Windows checkout (wp1): removes the throwaway
# clones and bundles under C:\home\code\extrode.com\bayan\tmp\ - t13-d3d398fd, feat-a4e62a40, any t14-*/feat-* directory
# that is a git repo, and win-*.bundle. Never touches the checkout itself or its branches.
# Dry run by default. -Execute acts.
#   pwsh -NoProfile scripts/cleanup/022-self-hosted-emitter-wp1.ps1
#   pwsh -NoProfile scripts/cleanup/022-self-hosted-emitter-wp1.ps1 -Execute
param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = 'C:\home\code\extrode.com\bayan'
$tmp = Join-Path $repo 'tmp'
$failed = $false

$targets = [System.Collections.Generic.List[string]]::new()
foreach ($n in 't13-d3d398fd', 'feat-a4e62a40') { $targets.Add((Join-Path $tmp $n)) }
if (Test-Path -LiteralPath $tmp) {
    foreach ($d in Get-ChildItem -LiteralPath $tmp -Directory -Force | Where-Object { $_.Name -like 't14-*' -or $_.Name -like 'feat-*' }) {
        if ((Test-Path -LiteralPath (Join-Path $d.FullName '.git')) -and -not $targets.Contains($d.FullName)) { $targets.Add($d.FullName) }
    }
    foreach ($f in Get-ChildItem -LiteralPath $tmp -File -Force -Filter 'win-*.bundle') { $targets.Add($f.FullName) }
}

Write-Host '== 1. clones and bundles'
foreach ($t in $targets) {
    $full = [System.IO.Path]::GetFullPath($t)
    if (-not $full.StartsWith($tmp + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "refuse: $full is outside $tmp. Left alone."
        $failed = $true
        continue
    }
    if (-not (Test-Path -LiteralPath $full)) { Write-Host "skip:  $full already gone"; continue }
    $rel = $full.Substring($repo.Length + 1).Replace('\', '/')
    git -C $repo ls-files --error-unmatch -- $rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "refuse: $rel is tracked by git. A cleanup script does not delete committed"
        Write-Host '        content; propose it as a commit instead. Left alone.'
        $failed = $true
        continue
    }
    if ($Execute) {
        Remove-Item -LiteralPath $full -Recurse -Force
        Write-Host "removed: $full"
    } else { Write-Host "would: remove $full" }
}

Write-Host ''
Write-Host '== remaining'
if (Test-Path -LiteralPath $tmp) {
    Get-ChildItem -LiteralPath $tmp -Force | Where-Object { $_.Name -like 't13-*' -or $_.Name -like 't14-*' -or $_.Name -like 'feat-*' -or $_.Name -like 'win-*.bundle' } | ForEach-Object { Write-Host $_.FullName }
}
if ($Execute -and $failed) { exit 1 }
exit 0
