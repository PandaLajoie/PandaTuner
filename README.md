# PandaTuner

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
| Navigateur de téléviseur (Tizen, webOS…) | **Direct** par le même relais |
| Lecteur natif (Jellyfin for Android TV, Jellyfin Media Player) | **Direct** à l'adresse d'origine |
| Autres (Swiftfin, applications inconnues) | **Proxy** : par le serveur, comme le M3U intégré |

Le **relais de playlists** règle deux problèmes courants des navigateurs :

- les sources qui n'autorisent pas les autres sites (CORS), comme Pluto TV, qui ne verrouille que ses playlists ;
- les playlists à adresses relatives.

Les adresses relayées sont signées par le serveur : le relais ne sert que les chaînes de la liste.

## Installation

1. Compiler (voir plus bas) ou récupérer `Jellyfin.Plugin.PandaTv.dll`.
2. La copier dans le dossier des plugins du serveur, dans un sous-dossier, par exemple
   `plugins/PandaTv_0.1.0.0/Jellyfin.Plugin.PandaTv.dll`, puis redémarrer Jellyfin.
3. *Tableau de bord → Télé en direct → Ajouter un syntoniseur* → **« M3U Panda (lecture directe) »**, avec l'adresse
   de la liste M3U. Les fournisseurs de guide (XMLTV) ne changent pas.

L'interface d'ajout de syntoniseur de Jellyfin est prévue pour M3U et HDHomeRun ; en cas de souci, le syntoniseur
peut être ajouté par l'API :

```http
POST /LiveTv/TunerHosts
{ "Type": "panda-m3u", "Url": "https://…/liste.m3u", "FriendlyName": "Panda", "IgnoreDts": false }
```

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

*Tableau de bord → Extensions → PandaTuner* :

- mode des chaînes sans attribut ;
- noms des applications « navigateur » et « lecteur natif » ;
- morceaux d'User-Agent qui désignent un navigateur de téléviseur.

## Compilation

.NET 10 et les paquets `Jellyfin.Controller` / `Jellyfin.Model` 12.0.0. Sans SDK installé, avec Docker :

```sh
docker run --rm -v "$PWD/Jellyfin.Plugin.PandaTv:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish -c Release -o /src/bin/publish
```

## Limites

- **Contournement expérimental.** Jellyfin 12.0 exclut le conteneur `hls` de la lecture directe. En direct, le plugin
  annonce `mp4` et signale le HLS par `TranscodingSubProtocol`, que l'interface web utilise pour activer hls.js.
  Une mise à jour de Jellyfin pourrait casser ce comportement.
- **Géoblocage.** En direct, c'est l'adresse IP de l'appareil qui compte, plus celle du serveur. Pour forcer le proxy
  pour une personne (par exemple à l'étranger), activer *« Forcer le transcodage des sources distantes »* dans ses
  paramètres d'utilisateur.
- **Applications.** Validé dans Chrome. Android TV et Android : d'après leur code source, pas encore testés en vrai.
  Swiftfin passe en proxy (il ne lit pas l'adresse d'origine). Navigateur de téléviseur Samsung : à tester ;
  probablement limité au réseau local.
- **Mixed content.** Si Jellyfin est servi en `https`, une source dont les segments sont en `http` sera bloquée par
  le navigateur.

## Licence

GPL-2.0, comme Jellyfin : le plugin reprend des parties de son code (lecture M3U, réglages du syntoniseur).
