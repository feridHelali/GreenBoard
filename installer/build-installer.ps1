# Construit l'installateur GreenBoard : tests → publication autonome (win-x64) → Inno Setup.
# Résultat : dist\GreenBoard-Setup-<version>.exe
#   pwsh -File installer\build-installer.ps1               (version lue dans InfiniteCanvas.App.csproj)
#   pwsh -File installer\build-installer.ps1 -SkipTests
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$appProj = Join-Path $root 'src\InfiniteCanvas.App\InfiniteCanvas.App.csproj'
$mcpProj = Join-Path $root 'src\InfiniteCanvas.Mcp\InfiniteCanvas.Mcp.csproj'
$publish = Join-Path $PSScriptRoot 'publish'

$version = ([xml](Get-Content $appProj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Version introuvable dans $appProj" }
Write-Host "GreenBoard $version" -ForegroundColor Green

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 introuvable. Installez-le : winget install JRSoftware.InnoSetup" }

if (-not $SkipTests) {
    Write-Host '1/4 Tests…' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'InfiniteCanvas.sln') -c Release --nologo -v q
    if ($LASTEXITCODE) { throw 'Des tests échouent : installateur non construit.' }
}

Write-Host '2/4 Publication de l''application (autonome, win-x64)…' -ForegroundColor Cyan
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
# Autonome : les amis n'ont pas besoin d'installer .NET
dotnet publish $appProj -c Release -r win-x64 --self-contained true -o (Join-Path $publish 'app') --nologo -v q `
    -p:DebugType=none -p:SatelliteResourceLanguages=fr
if ($LASTEXITCODE) { throw 'Échec de la publication de GreenBoard.' }

Write-Host '3/4 Publication du serveur MCP…' -ForegroundColor Cyan
dotnet publish $mcpProj -c Release -r win-x64 --self-contained true -o (Join-Path $publish 'mcp') --nologo -v q `
    -p:DebugType=none -p:SatelliteResourceLanguages=fr
if ($LASTEXITCODE) { throw 'Échec de la publication du serveur MCP.' }

Write-Host '4/4 Inno Setup…' -ForegroundColor Cyan
& $iscc "/DAppVersion=$version" /Q (Join-Path $PSScriptRoot 'GreenBoard.iss')
if ($LASTEXITCODE) { throw 'Échec de la compilation Inno Setup.' }

$setup = Get-Item (Join-Path $root "dist\GreenBoard-Setup-$version.exe")
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host ("Installateur : {0} ({1:N1} Mo)" -f $setup.FullName, ($setup.Length / 1MB)) -ForegroundColor Green
Write-Host "SHA-256 : $hash"
