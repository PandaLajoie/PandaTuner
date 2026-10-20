using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PandaTuner.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PandaTuner;

/// <summary>
/// Syntoniseur « PandaTuner ». Contrairement au M3U intégré (qui refuse la lecture directe des manifestes HLS),
/// il peut servir une chaîne en lecture directe : la vidéo va de la source à l'appareil sans passer par le serveur.
/// La décision est prise à chaque demande de lecture, selon le mode de la chaîne et l'application qui demande :
/// <list type="bullet">
/// <item>lecteur natif (Android TV…) : adresse d'origine ;</item>
/// <item>navigateur (ordinateur ou téléviseur) : playlists relayées par le serveur, segments en direct (<see cref="PlaylistRelay"/>) ;</item>
/// <item>autres applications, ou chaîne en <c>panda-mode="proxy"</c> : tout passe par le serveur, comme le M3U intégré.</item>
/// </list>
/// </summary>
public class PandaM3uTunerHost : ITunerHost, IConfigurableTunerHost
{
    public const string TunerType = "pandatuner";

    /// <summary>User-Agent envoyé aux sources (le même que le M3U intégré quand le syntoniseur n'en définit pas).</summary>
    public const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private readonly IServerConfigurationManager _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PandaM3uTunerHost> _logger;
    private readonly ConcurrentDictionary<string, List<PandaChannel>> _cache = new();

    public PandaM3uTunerHost(
        IServerConfigurationManager config,
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PandaM3uTunerHost> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    // Bilingue : le serveur ne connaît pas la langue de la personne qui lit ce nom.
    public string Name => "PandaTuner (M3U, direct play / lecture directe)";

    public string Type => TunerType;

    public bool IsSupported => true;

    /// <summary>Clé de signature des adresses relayées (générée au premier démarrage du plugin).</summary>
    public static string RelaySecret => PluginConfig.RelaySecret;

    private static string ChannelIdPrefix => TunerType + "_";

    private static PluginConfiguration PluginConfig => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    private static string DefaultMode => StreamMode.Normalize(PluginConfig.DefaultMode, StreamMode.Proxy);

    public async Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken)
    {
        var list = new List<ChannelInfo>();
        foreach (var tuner in GetTuners())
        {
            try
            {
                var channels = await GetChannels(tuner, enableCache, cancellationToken).ConfigureAwait(false);
                list.AddRange(channels.Select(c => c.Info).Where(c => !list.Any(l => l.Id == c.Id)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible de lire la liste M3U {Url}", tuner.Url);
            }
        }

        return list;
    }

    public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
    {
        var found = await FindChannelWithTunerAsync(channelId, cancellationToken).ConfigureAwait(false);
        return found is null ? [] : [CreateMediaSource(found.Value.Tuner, found.Value.Channel)];
    }

    /// <summary>
    /// Appelé seulement pour le mode proxy (source avec <c>RequiresOpening</c>) : Jellyfin ouvre puis sonde le flux.
    /// </summary>
    public async Task<ILiveStream> GetChannelStream(string channelId, string streamId, IList<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
    {
        // FileNotFoundException : même convention que les syntoniseurs intégrés pour « pas une de mes chaînes ».
        var found = await FindChannelWithTunerAsync(channelId, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException();

        _logger.LogInformation("Ouverture de {Name} en mode proxy", found.Channel.Info.Name);
        return new PandaLiveStream(CreateProxyMediaSource(found.Tuner, found.Channel), found.Tuner.Id);
    }

    public Task<List<TunerHostInfo>> DiscoverDevices(int discoveryDurationMs, CancellationToken cancellationToken)
        => Task.FromResult(new List<TunerHostInfo>());

    public async Task Validate(TunerHostInfo info)
    {
        // L'écran « Ajouter un syntoniseur » de Jellyfin n'affiche pas le champ d'adresse pour ce type :
        // l'adresse se règle dans la page de configuration du plugin.
        if (string.IsNullOrWhiteSpace(info.Url))
        {
            throw new ArgumentException(
                "Empty M3U list address: set it in Dashboard → Plugins → PandaTuner. / "
                + "Adresse de la liste M3U vide : réglez-la dans Tableau de bord → Extensions → PandaTuner.");
        }

        var content = await PandaM3uParser.ReadPlaylistAsync(info, CreateHttpClient(), CancellationToken.None).ConfigureAwait(false);
        if (PandaM3uParser.Parse(content, info.Id, ChannelIdPrefix, DefaultMode).Count == 0)
        {
            throw new ArgumentException("No channel found in the M3U list. / Aucune chaîne trouvée dans la liste M3U.");
        }
    }

    /// <summary>Retrouve une chaîne du plugin (pour le relais de playlists).</summary>
    public async Task<PandaChannel?> FindChannelAsync(string channelId, CancellationToken cancellationToken)
        => (await FindChannelWithTunerAsync(channelId, cancellationToken).ConfigureAwait(false))?.Channel;

    private async Task<(TunerHostInfo Tuner, PandaChannel Channel)?> FindChannelWithTunerAsync(string channelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(channelId) || !channelId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var tuner in GetTuners())
        {
            var channels = await GetChannels(tuner, true, cancellationToken).ConfigureAwait(false);
            var channel = channels.FirstOrDefault(c => string.Equals(c.Info.Id, channelId, StringComparison.OrdinalIgnoreCase));
            if (channel is not null)
            {
                return (tuner, channel);
            }
        }

        return null;
    }

    private MediaSourceInfo CreateMediaSource(TunerHostInfo tuner, PandaChannel channel)
    {
        var context = _httpContextAccessor.HttpContext;
        var (kind, client, userAgent) = ClientDetector.Detect(context, PluginConfig);
        var delivery = ClientDetector.ChooseDelivery(channel.Mode, kind);

        _logger.LogInformation(
            "Chaîne {Name} (mode {Mode}) pour « {Client} » ({Kind}) : {Delivery}. User-Agent : {UserAgent}",
            channel.Info.Name,
            channel.Mode,
            client,
            kind,
            delivery,
            userAgent);

        switch (delivery)
        {
            case Delivery.Origin:
                return CreateDirectMediaSource(tuner, channel, channel.Info.Path, isRemote: true);
            case Delivery.Relay when context is not null:
                var request = context.Request;
                var relayUrl = $"{request.Scheme}://{request.Host}{request.PathBase}/{PlaylistRelay.RoutePrefix}/{channel.Info.Id}/master.m3u8";
                // Navigateur de téléviseur : l'interface web n'y déclare pas la lecture « distante » (apphost.js).
                // La playlist venant du serveur lui-même, on la présente comme locale.
                return CreateDirectMediaSource(tuner, channel, relayUrl, isRemote: kind != ClientKind.TvBrowser);
            default:
                return CreateProxyMediaSource(tuner, channel);
        }
    }

    /// <summary>
    /// Lecture directe : l'appareil reçoit une adresse HLS (source ou relais) ; le serveur n'ouvre ni ne sonde le flux.
    /// </summary>
    private static MediaSourceInfo CreateDirectMediaSource(TunerHostInfo tuner, PandaChannel channel, string path, bool isRemote) => new()
    {
        Id = channel.Info.Id,
        Path = path,
        Protocol = MediaProtocol.Http,
        // EXPÉRIMENTAL. Jellyfin 12.0 exclut le conteneur « hls » de la lecture directe (StreamBuilder,
        // ManifestContainers). On annonce « mp4 » (accepté par tous les navigateurs) et on signale le HLS par
        // TranscodingSubProtocol, que le lecteur web utilise pour activer hls.js (utils/mediaSource.ts).
        // Android TV et Android devinent le HLS d'après l'adresse en .m3u8.
        Container = "mp4",
        TranscodingSubProtocol = MediaStreamProtocol.hls,
        IsRemote = isRemote,
        IsInfiniteStream = true,
        // Débit connu : sinon, pour une source locale, Jellyfin suppose 40 Mbit/s et peut refuser le direct.
        Bitrate = 8_000_000,
        RequiresOpening = false,
        RequiresClosing = false,
        SupportsProbing = false,
        SupportsDirectPlay = true,
        SupportsDirectStream = false,
        SupportsTranscoding = true,
        FallbackMaxStreamingBitrate = tuner.FallbackMaxStreamingBitrate,
        // Aucun en-tête obligatoire : sinon l'interface web refuse la lecture directe d'une source http.
        RequiredHttpHeaders = new Dictionary<string, string>(),
        // Index réels (pas -1) : sinon la décision de lecture ne trouve pas la piste audio par défaut
        // et conclut « AudioCodecNotSupported ».
        MediaStreams =
        [
            new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "h264", IsInterlaced = false, IsDefault = true },
            new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "aac", Channels = 2, IsDefault = true }
        ],
        DefaultAudioStreamIndex = 1
    };

    /// <summary>
    /// Proxy : mêmes réglages que le M3U intégré de Jellyfin 12.0 (M3UTunerHost.CreateMediaSourceInfo).
    /// Le flux est ouvert puis sondé (vraies pistes) ; ffmpeg le recopie ou le réencode selon l'appareil.
    /// </summary>
    private static MediaSourceInfo CreateProxyMediaSource(TunerHostInfo tuner, PandaChannel channel) => new()
    {
        Id = channel.Info.Id,
        Path = channel.Info.Path,
        Protocol = MediaProtocol.Http,
        IsRemote = true,
        IsInfiniteStream = true,
        RequiresOpening = true,
        RequiresClosing = true,
        RequiresLooping = tuner.EnableStreamLooping,
        SupportsProbing = true,
        SupportsDirectPlay = false,
        SupportsDirectStream = !tuner.EnableStreamLooping,
        SupportsTranscoding = true,
        IgnoreDts = tuner.IgnoreDts,
        ReadAtNativeFramerate = tuner.ReadAtNativeFramerate,
        FallbackMaxStreamingBitrate = tuner.FallbackMaxStreamingBitrate,
        UseMostCompatibleTranscodingProfile = !tuner.AllowFmp4TranscodingContainer,
        // Pas d'AnalyzeDurationMs : Jellyfin impose 3 s d'analyse à toute TV en direct (MediaSourceManager) ;
        // une valeur plus grande ne ferait qu'allonger l'attente avant l'analyse. Certaines chaînes Pluto n'ont
        // donc pas d'image en proxy (vidéo non détectée en 3 s) ; en direct, elles fonctionnent.
        RequiredHttpHeaders = new Dictionary<string, string>
        {
            ["User-Agent"] = string.IsNullOrWhiteSpace(tuner.UserAgent) ? BrowserUserAgent : tuner.UserAgent
        },
        // Index -1 comme le M3U intégré : pistes inconnues tant que le flux n'est pas sondé.
        MediaStreams =
        [
            new MediaStream { Type = MediaStreamType.Video, Index = -1, IsInterlaced = true },
            new MediaStream { Type = MediaStreamType.Audio, Index = -1 }
        ]
    };

    private async Task<List<PandaChannel>> GetChannels(TunerHostInfo tuner, bool enableCache, CancellationToken cancellationToken)
    {
        if (enableCache && _cache.TryGetValue(tuner.Id, out var cached))
        {
            return cached;
        }

        var content = await PandaM3uParser.ReadPlaylistAsync(tuner, CreateHttpClient(), cancellationToken).ConfigureAwait(false);
        var channels = PandaM3uParser.Parse(content, tuner.Id, ChannelIdPrefix, DefaultMode);
        _logger.LogInformation(
            "Liste {Url} : {Count} chaînes ({Proxy} en proxy forcé)",
            tuner.Url,
            channels.Count,
            channels.Count(c => c.Mode == StreamMode.Proxy));

        if (channels.Count > 0)
        {
            _cache[tuner.Id] = channels;
        }

        return channels;
    }

    private IEnumerable<TunerHostInfo> GetTuners()
        => _config.GetConfiguration<LiveTvOptions>("livetv").TunerHosts
            .Where(t => string.Equals(t.Type, TunerType, StringComparison.OrdinalIgnoreCase));

    private HttpClient CreateHttpClient() => _httpClientFactory.CreateClient(NamedClient.Default);
}
