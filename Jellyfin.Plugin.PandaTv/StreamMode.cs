using System;

namespace Jellyfin.Plugin.PandaTv;

/// <summary>
/// Modes de diffusion d'une chaîne (attribut <c>panda-mode</c>).
/// </summary>
public static class StreamMode
{
    /// <summary>Lecture directe pour les applications qui la gèrent (navigateurs d'ordinateur et lecteurs natifs).</summary>
    public const string Direct = "direct";

    /// <summary>Lecture directe seulement pour les lecteurs natifs : pour les sources qui bloquent les navigateurs (CORS, ex. Pluto).</summary>
    public const string App = "app";

    /// <summary>Le serveur lit la source avec ffmpeg et la retransmet (comportement du M3U intégré).</summary>
    public const string Proxy = "proxy";

    public static string Normalize(string? value, string fallback)
    {
        foreach (var mode in new[] { Direct, App, Proxy })
        {
            if (string.Equals(value?.Trim(), mode, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        return fallback;
    }
}
