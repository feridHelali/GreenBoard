# Génère 1000 clés de licence GreenBoard
# Format : GB-XXXXX-XXXXX-XXXXX-XXXXX (20 caractères alphanumériques, sans O, 0, I, L)
param([int]$Count = 1000, [string]$Output = "cles_licences.txt")

$AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray()
$rng = [System.Random]::New()

function New-LicenceKey {
    $parts = @()
    for ($i = 0; $i -lt 4; $i++) {
        $part = ""
        for ($j = 0; $j -lt 5; $j++) {
            $part += $AllowedChars[$rng.Next(0, $AllowedChars.Length)]
        }
        $parts += $part
    }
    return "GB-$($parts -join '-')"
}

$clefs = @()
$seen = @{}
while ($clefs.Count -lt $Count) {
    $k = New-LicenceKey
    if (-not $seen.ContainsKey($k)) {
        $seen[$k] = $true
        $clefs += $k
    }
}

$outPath = Join-Path $PSScriptRoot $Output
Set-Content -Path $outPath -Value $clefs -Encoding UTF8

Write-Host "$($clefs.Count) clés de licence générées dans $outPath"
$clefs | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" }
