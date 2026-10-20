using System;
using System.Linq;
using Jellyfin.Plugin.PandaTv.Configuration;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.PandaTv;

/// <summary>
/// Type d'application qui demande la lecture.
/// </summary>
public enum ClientKind
{
    /// <summary>Application inconnue, Swiftfin… : toujours en proxy (mode sûr).</summary>
    Other,

    /// <summary>Navigateur d'ordinateur (hls.js).</summary>
    Browser,

    /// <summary>Navigateur de téléviseur (Tizen, webOS…) : l'interface web n'y déclare pas la lecture distante.</summary>
    TvBrowser,

    /// <summary>Lecteur natif (ExoPlayer, mpv) : lit l'adresse d'origine, sans contrainte CORS.</summary>
    Native
}

/// <summary>
/// Comment la chaîne est servie à l'appareil.
/// </summary>
public enum Delivery
{
    /// <summary>Tout passe par le serveur (ffmpeg), comme le M3U intégré.</summary>
    Proxy,

    /// <summary>Lecture directe à l'adresse d'origine.</summary>
    Origin,

    /// <summary>Lecture directe des segments ; seules les playlists passent par le serveur.</summary>
    Relay
}

/// <summary>
/// Reconnaît l'application à partir de la requête HTTP en cours (nom de client authentifié + User-Agent).
/// </summary>
public static class ClientDetector
{
    // Type de revendication posé par Jellyfin à l'authentification (Jellyfin.Api InternalClaimTypes.Client).
    private const string ClientClaimType = "Jellyfin-Client";

    public static (ClientKind Kind, string Client, string UserAgent) Detect(HttpContext? context, PluginConfiguration config)
    {
        if (context is null)
        {
            return (ClientKind.Other, string.Empty, string.Empty);
        }

        var client = context.User.FindFirst(ClientClaimType)?.Value ?? string.Empty;
        var userAgent = context.Request.Headers.UserAgent.ToString();

        if (Matches(client, config.NativeClients))
        {
            return (ClientKind.Native, client, userAgent);
        }

        if (Matches(client, config.BrowserClients))
        {
            var isTv = Split(config.TvUserAgents).Any(tv => userAgent.Contains(tv, StringComparison.OrdinalIgnoreCase));
            return (isTv ? ClientKind.TvBrowser : ClientKind.Browser, client, userAgent);
        }

        return (ClientKind.Other, client, userAgent);
    }

    /// <summary>
    /// Choisit la diffusion selon le mode de la chaîne et l'application.
    /// </summary>
    public static Delivery ChooseDelivery(string channelMode, ClientKind kind)
    {
        if (channelMode == StreamMode.Proxy)
        {
            return Delivery.Proxy;
        }

        return kind switch
        {
            ClientKind.Native => Delivery.Origin,
            // Mode « app » : direct réservé aux lecteurs natifs.
            ClientKind.Browser or ClientKind.TvBrowser when channelMode == StreamMode.Direct => Delivery.Relay,
            _ => Delivery.Proxy
        };
    }

    private static bool Matches(string client, string list)
        => client.Length > 0 && Split(list).Any(c => string.Equals(c, client, StringComparison.OrdinalIgnoreCase));

    private static string[] Split(string list)
        => list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
