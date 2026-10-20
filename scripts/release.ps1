# Publie une version de PandaTuner : compile, crée le .zip, met à jour manifest.json (dépôt de plugins Jellyfin),
# crée la release GitHub avec le .zip, puis pousse le manifest.
#
# Usage :
#   .\scripts\release.ps1 -Version 0.2.0 -Changelog "Ce qui change dans cette version"
#   .\scripts\release.ps1 -Version 0.2.0 -Changelog "…" -NoPublish   (prépare tout sans rien envoyer)
#
# Prérequis : Docker Desktop démarré (compilation), gh connecté au compte PandaLajoie.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Changelog,
    [string]$TargetAbi = '12.0.0.0',
    [string]$CommitTrailer = '',
    [switch]$NoPublish
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version attendue au format 1.2.3 (reçu : $Version)" }

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Jellyfin.Plugin.PandaTuner'
$assemblyVersion = "$Version.0"
$tag = "v$Version"
$repo = 'PandaLajoie/PandaTuner'
$zipName = "pandatuner_$assemblyVersion.zip"
$gh = if (Test-Path 'C:\Program Files\GitHub CLI\gh.exe') { 'C:\Program Files\GitHub CLI\gh.exe' } else { 'gh' }

# Fiche du plugin dans le catalogue Jellyfin (GUID : celui de Plugin.cs, ne jamais le changer).
$package = [ordered]@{
    guid        = '8e7a3c52-4b1d-4f3a-9c2e-6d5b0a1f7e93'
    name        = 'PandaTuner'
    description = 'Syntoniseur M3U avec lecture directe des chaînes IPTV : la vidéo ne passe plus par le serveur.'
    overview    = 'Ajoute un syntoniseur « PandaTuner ». Selon l''application, chaque chaîne est servie en direct (navigateurs, Android TV, Jellyfin Media Player) ou par le serveur (autres applications). Relais de playlists pour les sources qui bloquent les navigateurs (ex. Pluto TV).'
    owner       = 'PandaLajoie'
    category    = 'Live TV'
    imageUrl    = "https://raw.githubusercontent.com/$repo/main/images/logo.png"
}

Write-Host "== Compilation $assemblyVersion"
docker run --rm -v "${project}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 `
    dotnet publish -c Release -o /src/bin/publish "-p:AssemblyVersion=$assemblyVersion" "-p:FileVersion=$assemblyVersion"
if ($LASTEXITCODE -ne 0) { throw 'La compilation a échoué.' }

Write-Host "== Archive $zipName"
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist $zipName
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $project 'bin\publish\Jellyfin.Plugin.PandaTuner.dll') -DestinationPath $zip
$checksum = (Get-FileHash $zip -Algorithm MD5).Hash.ToLowerInvariant()
Write-Host "   MD5 $checksum"

Write-Host '== manifest.json'
$manifestPath = Join-Path $root 'manifest.json'
$versions = @()
if (Test-Path $manifestPath) {
    $existing = @(Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json)
    $versions = @($existing[0].versions | Where-Object { $_.version -ne $assemblyVersion })
}
$newVersion = [ordered]@{
    version   = $assemblyVersion
    changelog = $Changelog
    targetAbi = $TargetAbi
    sourceUrl = "https://github.com/$repo/releases/download/$tag/$zipName"
    checksum  = $checksum
    timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}
$package.versions = @($newVersion) + $versions
$json = ConvertTo-Json -InputObject @($package) -Depth 6
# UTF-8 sans BOM : accents conservés, lisible par tous les lecteurs JSON.
[IO.File]::WriteAllText($manifestPath, $json + "`n", (New-Object Text.UTF8Encoding($false)))

if ($NoPublish) {
    Write-Host "Préparé sans publier : $zip et manifest.json."
    return
}

# Windows PowerShell 5.1 traite tout message de gh/git sur stderr (même un simple avertissement) comme une erreur
# fatale quand ErrorActionPreference vaut Stop : on se fie au code de sortie à partir d'ici.
$ErrorActionPreference = 'Continue'

Write-Host "== Release GitHub $tag"
& $gh release create $tag $zip --repo $repo --title "PandaTuner $Version" --notes $Changelog
if ($LASTEXITCODE -ne 0) { throw 'La création de la release a échoué.' }

Write-Host '== Envoi du manifest'
$message = "Version $Version`n`n$Changelog"
if ($CommitTrailer) { $message += "`n`n$CommitTrailer" }
git -C $root add manifest.json
git -C $root commit -q -m $message
git -C $root push -q origin main
if ($LASTEXITCODE -ne 0) { throw 'L''envoi du manifest a échoué.' }

Write-Host ''
Write-Host "Publié. Adresse du dépôt de plugins pour Jellyfin :"
Write-Host "  https://raw.githubusercontent.com/$repo/main/manifest.json"
