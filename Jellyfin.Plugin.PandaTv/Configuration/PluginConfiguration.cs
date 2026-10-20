using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PandaTv.Configuration;

/// <summary>
/// Réglages du plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets le mode des chaînes sans attribut <c>panda-mode</c> : « direct », « app » ou « proxy ».
    /// </summary>
    public string DefaultMode { get; set; } = StreamMode.Direct;

    /// <summary>
    /// Gets or sets les applications « navigateur » (lecteur hls.js, soumis au CORS), séparées par des virgules.
    /// Comparées au nom d'application envoyé par le client.
    /// </summary>
    public string BrowserClients { get; set; } = "Jellyfin Web";

    /// <summary>
    /// Gets or sets les applications à lecteur natif (pas de CORS), séparées par des virgules.
    /// </summary>
    public string NativeClients { get; set; } = "Jellyfin for Android TV,Jellyfin Media Player";

    /// <summary>
    /// Gets or sets les morceaux d'User-Agent qui désignent un navigateur de téléviseur : la lecture directe n'y
    /// fonctionne pas (l'interface web n'y déclare pas la lecture distante), ils restent donc en proxy.
    /// </summary>
    public string TvUserAgents { get; set; } = "Tizen,Web0S,webOS,SMART-TV,SmartTV,NetCast,HbbTV";

    /// <summary>
    /// Gets or sets la clé de signature des adresses relayées. Générée au premier démarrage ; ne pas la partager.
    /// </summary>
    public string RelaySecret { get; set; } = string.Empty;
}
