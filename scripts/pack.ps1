param(
    [string]$Configuration = 'Release',
    [string]$OMDir = 'C:\Program Files (x86)\Steam\steamapps\common\Opus Magnum',
    [switch]$Deploy   # also copy the zip into <OMDir>\Mods
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\ZlbbLeaderboard\ZlbbLeaderboard.csproj'

dotnet build $proj -c $Configuration -p:OMDir="$OMDir" --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$out = Join-Path $root "src\ZlbbLeaderboard\bin\$Configuration\net452"
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$stage = Join-Path $root "dist\stage"
$zip = Join-Path $root "dist\ZlbbLeaderboard_$version.zip"
Remove-Item $stage, $zip -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

# mod DLLs + manifest at the zip root (dependencies already shipped with the game are not copied)
Copy-Item (Join-Path $out 'quintessential.yaml') $stage
Get-ChildItem $out -Filter '*.dll' | Where-Object { $_.Name -like 'Zlbb*' } | Copy-Item -Destination $stage
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Write-Host "Packed $zip"

if ($Deploy) {
    $mods = Join-Path $OMDir 'Mods'
    if (-not (Test-Path $mods)) { throw "Mods folder not found: $mods" }
    Get-ChildItem $mods -Filter 'ZlbbLeaderboard_*.zip' | Remove-Item -Force
    Copy-Item $zip $mods
    Write-Host "Deployed to $mods"
}
