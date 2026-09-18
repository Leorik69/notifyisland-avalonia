param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Proj = Join-Path $Root "NotifyIsland.Av.csproj"
$Dist = Join-Path $Root "dist"
$Pub = Join-Path $Dist "win-x64"
$Iss = Join-Path $Root "setup\notifyisland.iss"
$Iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $Iscc)) { $Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }

New-Item -ItemType Directory -Force -Path $Pub | Out-Null
dotnet publish $Proj -c $Configuration -r win-x64 --self-contained true -o $Pub
Get-ChildItem $Pub -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
Remove-Item (Join-Path $Pub "notifyisland.settings.json") -ErrorAction SilentlyContinue
Remove-Item (Join-Path $Pub "notifyisland.weather.json") -ErrorAction SilentlyContinue

$Zip = Join-Path $Dist "NotifyIsland-portable-win-x64.zip"
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path (Join-Path $Pub "*") -DestinationPath $Zip -CompressionLevel Optimal

if (-not (Test-Path $Iscc)) { throw "ISCC.exe not found" }
& $Iscc "/O$Dist" "/DPublishDir=$Pub" $Iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }

Write-Host "Artifacts:"
Get-ChildItem $Dist -File | ForEach-Object { "{0}  {1:N0} bytes" -f $_.Name, $_.Length }
