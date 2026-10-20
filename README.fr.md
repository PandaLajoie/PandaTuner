# PandaTuner

[English](README.md) · **Français**

![PandaTuner](images/logo.png)

Plugin Jellyfin qui ajoute un syntoniseur M3U **capable de lecture directe** : la vidéo des chaînes IPTV va de la
source à l'appareil, sans passer par le serveur. Le serveur ne fournit que le guide, l'adresse du flux et, pour les
navigateurs, les playlists (quelques Ko).

Testé avec **Jellyfin 12.0**. Prototype (version 0.1) : voir [Limites](#limites).

## Pourquoi

Le syntoniseur M3U intégré de Jellyfin fait toujours passer les flux HLS par le serveur (ffmpeg). Chaque personne qui
regarde consomme donc la bande passante du serveur, en réception et en envoi. Aucun réglage ni attribut M3U ne permet
de changer ça : Jellyfin refuse volontairement la lecture directe des playlists HLS, à deux endroits
(`M3UTunerHost` et `StreamBuilder`).

## Fonctionnement

À chaque demande de lecture, le plugin regarde **quelle application** demande et choisit :

| Application | Diffusion |
|---|---|
| Navigateur d'ordinateur (Jellyfin Web) | **Direct** : playlists relayées par le serveur, segments vidéo depuis la source |
| Navigateur de téléviseur (Tizen, webOS…) et application TV Samsung (Jellyfin for Tizen) | **Direct** par le même relais |
| Lecteur natif (Jellyfin for Android TV, Jellyfin Media Player) | **Direct** à l'adresse d'origine |
| Autres (Swiftfin, applications inconnues) | **Proxy** : par le serveur, comme le M3U intégré |

Le **relais de playlists** règle deux problèmes courants des navigateurs :

- les sources qui n'autorisent pas les autres sites (CORS), comme Pluto TV, qui ne verrouille que ses playlists ;
- les playlists à adresses relatives.

Les adresses relayées sont signées par le serveur : le relais ne sert que les chaînes de la liste.

## Installation

### Par le dépôt de plugins (recommandé)

1. *Tableau de bord → Extensions → Dépôts* → **+**, avec l'adresse :
   ```
   https://raw.githubusercontent.com/PandaLajoie/PandaTuner/main/manifest.json
   ```
2. *Catalogue* → **PandaTuner** → *Installer*, puis redémarrer Jellyfin.
3. Configurer la liste de chaînes : voir [Configuration](#configuration).

Les nouvelles versions apparaissent ensuite dans le catalogue. Pour mettre à jour : *Catalogue → PandaTuner*, choisir
la version, *Installer*, puis redémarrer (ou lancer la tâche planifiée *Mettre à jour les extensions*).

### À la main

Copier `Jellyfin.Plugin.PandaTuner.dll` (dans le `.zip` des [releases](https://github.com/PandaLajoie/PandaTuner/releases))
dans un sous-dossier des plugins du serveur, par exemple `plugins/PandaTuner_0.1.2.0/`, puis redémarrer Jellyfin
et passer à la [Configuration](#configuration).

## Liste M3U

Mêmes attributs que le M3U intégré (`tvg-id`, `tvg-chno`, `tvg-logo`, `group-title`), plus `panda-mode` :

```m3u
#EXTINF:-1 tvg-chno="11" tvg-id="..." panda-mode="direct",LCN
https://…/lcn.m3u8
```

| `panda-mode` | Effet |
|---|---|
| `direct` | Direct pour les navigateurs et les lecteurs natifs (défaut) |
| `app` | Direct seulement pour les lecteurs natifs |
| `proxy` | Toujours par le serveur |

Le syntoniseur intégré ignore cet attribut : la même liste reste utilisable sans le plugin.

## Configuration

Tout se règle dans **Tableau de bord → Extensions → PandaTuner**.

> L'écran *Télé en direct → Ajouter un syntoniseur* de Jellyfin ne convient pas : avec le type « PandaTuner », il
> n'affiche pas le champ d'adresse de la liste, et l'enregistrement échoue.

### 1. Liste de chaînes

1. **Adresse de la liste M3U** : l'adresse `http(s)://…` de la liste (ou un chemin de fichier sur le serveur).
2. **Nom du syntoniseur** : libre (par défaut « PandaTuner »).
3. **Enregistrer le syntoniseur** : crée le syntoniseur PandaTuner, ou le met à jour s'il existe déjà.
   Le serveur télécharge la liste pour la vérifier : une adresse inaccessible ou sans chaîne est refusée.
4. *Tableau de bord → Télé en direct* → **Rafraîchir les données de guide** pour afficher les chaînes.

### 2. Guide des programmes

Rien de spécial : les fournisseurs XMLTV de *Télé en direct* s'appliquent comme avec le M3U intégré
(correspondance par `tvg-id`).

### 3. Remplacer le syntoniseur M3U intégré

Si la même liste était déjà chargée par le syntoniseur « M3U » de Jellyfin, chaque chaîne apparaît **deux fois**.
Une fois PandaTuner vérifié, supprimer l'ancien syntoniseur dans *Télé en direct*.

### 4. Lecture (facultatif)

- **Mode des chaînes sans attribut** `panda-mode` : `direct` par défaut.
- **Applications « navigateur »** et **à lecteur natif** : noms d'applications (tels qu'envoyés par le client)
  qui ont droit au direct. Les autres passent par le serveur. Applications « navigateur » par défaut :
  `Jellyfin Web,Jellyfin for Tizen` (l'application TV Samsung utilise le même lecteur web ; ajoutée automatiquement
  aux réglages existants par la version 0.1.3).
- **User-Agent de navigateurs de téléviseur** : morceaux d'User-Agent (Tizen, webOS…) qui désignent un navigateur
  de téléviseur ; il est servi en direct par le relais, mais présenté comme une source locale.

## Compilation

.NET 10 et les paquets `Jellyfin.Controller` / `Jellyfin.Model` 12.0.0. Sans SDK installé, avec Docker :

```sh
docker run --rm -v "$PWD/Jellyfin.Plugin.PandaTuner:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish -c Release -o /src/bin/publish
```

## Publier une version

```powershell
.\scripts\release.ps1 -Version 0.2.0 -Changelog "Ce qui change"
```

Compile, crée le `.zip`, met à jour `manifest.json`, crée la release GitHub et pousse le manifest.
`-NoPublish` prépare tout sans rien envoyer.

## Limites

- **Contournement expérimental.** Jellyfin 12.0 exclut le conteneur `hls` de la lecture directe. En direct, le plugin
  annonce `mp4` et signale le HLS par `TranscodingSubProtocol`, que l'interface web utilise pour activer hls.js.
  Une mise à jour de Jellyfin pourrait casser ce comportement.
- **Géoblocage.** En direct, c'est l'adresse IP de l'appareil qui compte, plus celle du serveur. Pour forcer le proxy
  pour une personne (par exemple à l'étranger), activer *« Forcer le transcodage des sources distantes »* dans ses
  paramètres d'utilisateur.
- **Applications.** Validé dans Chrome. Android TV et Android : d'après leur code source, pas encore testés en vrai.
  Swiftfin passe en proxy (il ne lit pas l'adresse d'origine). TV Samsung (navigateur et application Jellyfin for
  Tizen) : testée en vrai.
- **Téléviseurs : réseau local seulement.** Sur une TV, le lecteur web n'accepte le relais que si Jellyfin considère
  la TV comme étant sur son réseau local. Ouvrir Jellyfin sur la TV par une adresse locale (par exemple
  `http://192.168.1.10:8096`), pas par son nom de domaine public ; sinon, seule l'image de la chaîne s'affiche. Si la
  TV doit passer par l'adresse publique, ajouter l'IP publique de la maison aux *Réseaux LAN* de Jellyfin
  (*Tableau de bord → Réseau*).
- **Mode proxy et certaines chaînes Pluto.** Jellyfin impose 3 s d'analyse à toute TV en direct ; pour quelques
  chaînes Pluto, ffmpeg n'y détecte pas la vidéo → son sans image en proxy (même limite que le M3U intégré).
  En direct, ces chaînes fonctionnent.
- **Mixed content.** Si Jellyfin est servi en `https`, une source dont les segments sont en `http` sera bloquée par
  le navigateur.

## Licence

GPL-2.0, comme Jellyfin : le plugin reprend des parties de son code (lecture M3U, réglages du syntoniseur).
