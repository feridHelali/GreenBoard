# Génère 1000 clés de licence GreenBoard
# Format : GB-XXXXX-XXXXX-XXXXX-XXXXX (20 caractères alphanumériques, sans O, 0, I, L)
# Chaque groupe de 5 se termine par un caractère de contrôle (checksum)
# Checksum : 5e caractere = alphabet[(somme des indices des 4 premiers) mod longueur(alphabet)]
# Coherant avec IsValidLicenceKey() dans GreenBoard.iss
param([int]$Count = 1000, [string]$Output = "cles_licences.txt")

$AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray()
$AlphabetLen = $AllowedChars.Length
$rng = [System.Random]::New()

function New-LicenceKey {
    $parts = @()
    for ($i = 0; $i -lt 4; $i++) {
        $group4 = ""
        for ($j = 0; $j -lt 4; $j++) {
            $group4 += $AllowedChars[$rng.Next(0, $AllowedChars.Length)]
        }
        # Caractere de controle (checksum) en 5e position
        $sum = 0
        foreach ($c in $group4.ToCharArray()) {
            $sum += [Array]::IndexOf($AllowedChars, $c)
        }
        $checksum = $AllowedChars[$sum % $AlphabetLen]
        $parts += ($group4 + $checksum)
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

Write-Host "$($clefs.Count) cles de licence generees dans $outPath"
$clefs | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" }
