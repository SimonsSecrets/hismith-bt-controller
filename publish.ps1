# Publishes the release: a single self-contained HismithController.exe.
# Release settings (RID, single-file, compression, native-lib packing, no pdb)
# live in HismithController.csproj, so this script just drives the publish.
#
# Usage:  .\publish.ps1            publish
#         .\publish.ps1 -Open      publish, then open the output folder
#         .\publish.ps1 -Clean     wipe bin/obj first (use if you hit a stale-obj build error)

[CmdletBinding()]
param(
    [switch]$Open,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\HismithController\HismithController.csproj'

if ($Clean) {
    Write-Host 'Cleaning bin/obj...' -ForegroundColor Cyan
    $base = Join-Path $PSScriptRoot 'src\HismithController'
    foreach ($dir in @('bin', 'obj')) {
        $path = Join-Path $base $dir
        if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    }
}

Write-Host 'Publishing release...' -ForegroundColor Cyan
dotnet publish $project -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$publishDir = Join-Path $PSScriptRoot 'src\HismithController\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish'
$exe = Join-Path $publishDir 'HismithController.exe'

Write-Host ''
Write-Host 'Published:' -ForegroundColor Green
Write-Host "  $exe"
if (Test-Path $exe) {
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "  ($sizeMb MB, self-contained single file)"
}

if ($Open) { Start-Process $publishDir }
