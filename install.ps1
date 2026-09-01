param(
    [string]$GameDirectory = 'D:\Chill with You\Chill with You'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Join-Path $projectRoot 'dist\AICompanion'
$resolvedGame = [IO.Path]::GetFullPath($GameDirectory)
$gameExe = Join-Path $resolvedGame 'Chill With You.exe'
$managed = Join-Path $resolvedGame 'Chill With You_Data\Managed\Assembly-CSharp.dll'

if (-not (Test-Path -LiteralPath $gameExe) -or -not (Test-Path -LiteralPath $managed)) {
    throw "The target is not a compatible Chill With You Mono installation: $resolvedGame"
}
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'BepInEx\plugins\AICompanion\AICompanion.Plugin.dll'))) {
    throw 'Build output is missing. Run build.ps1 first.'
}

Copy-Item -Path (Join-Path $packageRoot '*') -Destination $resolvedGame -Recurse -Force
Write-Host "Installed AI Companion into $resolvedGame"
Write-Host 'Start Chill With You once, open the AI button, then configure the API endpoint, model, and key.'
