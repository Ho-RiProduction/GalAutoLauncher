<#
.SYNOPSIS
    生成一份干净的发布包（不含个人游戏路径），用于上传 GitHub Release。
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\GalAutoLauncher-win-x64'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $root $OutputDirectory

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

New-Item -ItemType Directory -Path $stage -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $root 'publish\GalAutoLauncher.exe') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'src\GalAutoLauncher\config.json') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'src\GalAutoLauncher\games.json') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'publish\install.cmd') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'publish\uninstall.cmd') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage

Get-ChildItem -LiteralPath $stage | Select-Object Name, Length | Format-Table -AutoSize
