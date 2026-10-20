# PandaTuner

**English** · [Français](README.fr.md)

![PandaTuner](images/logo.png)

Jellyfin plugin that adds an M3U tuner **capable of direct play**: IPTV channel video goes from the source straight
to the device, without going through the server. The server only provides the guide, the stream address and, for
browsers, the playlists (a few KB).

Tested with **Jellyfin 12.0**. Prototype (version 0.1): see [Limitations](#limitations).

## Why

Jellyfin's built-in M3U tuner always routes HLS streams through the server (ffmpeg). Every viewer therefore uses the
server's bandwidth, both downstream and upstream. No setting or M3U attribute changes that: Jellyfin deliberately
refuses direct play of HLS playlists, in two places (`M3UTunerHost` and `StreamBuilder`).

## How it works

For each playback request, the plugin looks at **which app** is asking and chooses:

| App | Delivery |
|---|---|
| Desktop browser (Jellyfin Web) | **Direct**: playlists relayed by the server, video segments from the source |
| TV browser (Tizen, webOS…) and the Samsung TV app (Jellyfin for Tizen) | **Direct** through the same relay |
| Native player (Jellyfin for Android TV, Jellyfin Media Player) | **Direct** to the original address |
| Others (Swiftfin, unknown apps) | **Proxy**: through the server, like the built-in M3U tuner |

The **playlist relay** solves two common browser problems:

- sources that don't allow other websites (CORS), such as Pluto TV, which only locks its playlists;
- playlists with relative addresses.

Relayed addresses are signed by the server: the relay only serves channels from the list.

## Installation

### From the plugin repository (recommended)

1. *Dashboard → Plugins → Repositories* → **+**, with the address:
   ```
   https://raw.githubusercontent.com/PandaLajoie/PandaTuner/main/manifest.json
   ```
2. *Catalog* → **PandaTuner** → *Install*, then restart Jellyfin.
3. Set up the channel list: see [Configuration](#configuration).

New versions then show up in the catalog. To update: *Catalog → PandaTuner*, pick the version, *Install*, then
restart (or run the *Update Plugins* scheduled task).

### Manually

Copy `Jellyfin.Plugin.PandaTuner.dll` (in the `.zip` from the [releases](https://github.com/PandaLajoie/PandaTuner/releases))
into a subfolder of the server's plugin directory, e.g. `plugins/PandaTuner_0.1.2.0/`, then restart Jellyfin and
go to [Configuration](#configuration).

## M3U list

Same attributes as the built-in M3U tuner (`tvg-id`, `tvg-chno`, `tvg-logo`, `group-title`), plus `panda-mode`:

```m3u
#EXTINF:-1 tvg-chno="11" tvg-id="..." panda-mode="direct",LCN
https://…/lcn.m3u8
```

| `panda-mode` | Effect |
|---|---|
| `direct` | Direct for browsers and native players (default) |
| `app` | Direct for native players only |
| `proxy` | Always through the server |

The built-in tuner ignores this attribute: the same list still works without the plugin.

### Where to find a list

- **[iptv-org/iptv](https://github.com/iptv-org/iptv)** (recommended): public channels from all over the world, sorted
  by country, language or category. Pick a smaller list rather than the full one (over 10,000 channels), for
  example `https://iptv-org.github.io/iptv/countries/ca.m3u` (Canada) or
  `https://iptv-org.github.io/iptv/languages/fra.m3u` (French). Some channels are offline or geo-blocked.
- **[PandaM3uList](https://github.com/PandaLajoie/PandaM3uList)**: the author's list, French-language channels
  only (Québec and France), with guide IDs checked:
  `https://raw.githubusercontent.com/PandaLajoie/PandaM3uList/main/PandaM3uList.m3u`.

## Configuration

Everything is set in **Dashboard → Plugins → PandaTuner**.

> Jellyfin's *Live TV → Add tuner device* screen doesn't work here: with the "PandaTuner" type it doesn't show the
> list address field, and saving fails.

### 1. Channel list

1. **M3U list address**: the `http(s)://…` address of the list (or a file path on the server).
2. **Tuner name**: anything (defaults to "PandaTuner").
3. **Save tuner**: creates the PandaTuner tuner, or updates it if it already exists.
   The server downloads the list to check it: an unreachable address or a list without channels is rejected.
4. *Dashboard → Live TV* → **Refresh guide data** to show the channels.

### 2. Program guide

Nothing special: the XMLTV providers in *Live TV* apply as with the built-in M3U tuner (matched by `tvg-id`).

### 3. Replacing the built-in M3U tuner

If the same list was already loaded by Jellyfin's "M3U" tuner, every channel shows up **twice**.
Once PandaTuner is working, delete the old tuner in *Live TV*.

### 4. Playback (optional)

- **Mode for channels without** a `panda-mode` attribute: `direct` by default.
- **"Browser" apps** and **native player apps**: app names (as sent by the client) allowed to play directly.
  Other apps go through the server. Default "browser" apps: `Jellyfin Web,Jellyfin for Tizen` (the Samsung TV app
  runs the same web player; added automatically to existing settings by version 0.1.3).
- **TV browser User-Agents**: User-Agent fragments (Tizen, webOS…) that identify a TV browser; it is served directly
  through the relay, but presented as a local source.

## Building

.NET 10 and the `Jellyfin.Controller` / `Jellyfin.Model` 12.0.0 packages. Without an SDK installed, with Docker:

```sh
docker run --rm -v "$PWD/Jellyfin.Plugin.PandaTuner:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish -c Release -o /src/bin/publish
```

## Publishing a version

```powershell
.\scripts\release.ps1 -Version 0.2.0 -Changelog "What changes"
```

Builds, creates the `.zip`, updates `manifest.json`, creates the GitHub release and pushes the manifest.
`-NoPublish` prepares everything without sending anything.

## Limitations

- **Experimental workaround.** Jellyfin 12.0 excludes the `hls` container from direct play. For direct play, the
  plugin announces `mp4` and signals HLS through `TranscodingSubProtocol`, which the web client uses to enable hls.js.
  A Jellyfin update could break this behavior.
- **Geo-blocking.** With direct play, the device's IP address is what counts, no longer the server's. To force proxy
  for someone (e.g. abroad), enable *"Force transcoding of remote media sources"* in their user settings.
- **Apps.** Validated in Chrome. Android TV and Android: based on their source code, not yet tested for real.
  Swiftfin uses proxy (it doesn't read the original address). Samsung TV (browser and Jellyfin for Tizen app):
  tested for real.
- **TVs: local network only.** On a TV, the web player only accepts the relay if Jellyfin considers the TV to be on
  its local network. Open Jellyfin on the TV through a local address (e.g. `http://192.168.1.10:8096`), not through
  its public domain; otherwise only the channel image shows. If the TV must use the public address, add your home
  public IP to Jellyfin's *LAN networks* (*Dashboard → Networking*).
- **Proxy mode and some Pluto channels.** Jellyfin enforces 3 s of analysis for all live TV; for a few Pluto
  channels, ffmpeg doesn't detect the video in that time → audio without picture in proxy (same limitation as the
  built-in M3U tuner). With direct play, these channels work.
- **Mixed content.** If Jellyfin is served over `https`, a source whose segments use `http` will be blocked by the
  browser.

## License

GPL-2.0, like Jellyfin: the plugin reuses parts of its code (M3U parsing, tuner settings).
