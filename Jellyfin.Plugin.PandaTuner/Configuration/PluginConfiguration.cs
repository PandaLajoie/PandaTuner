using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PandaTuner.Configuration;

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
    public string BrowserClients { get; set; } = "Jellyfin Web,Jellyfin for Tizen";

    /// <summary>
    /// Gets or sets les applications à lecteur natif (pas de CORS), séparées par des virgules.
    /// </summary>
    public string NativeClients { get; set; } = "Jellyfin for Android TV,Jellyfin Media Player";

    /// <summary>
    /// Gets or sets les morceaux d'User-Agent qui désignent un navigateur de téléviseur : l'interface web n'y déclare
    /// pas la lecture distante, le relais y est donc présenté comme une source locale.
    /// </summary>
    public string TvUserAgents { get; set; } = "Tizen,Web0S,webOS,SMART-TV,SmartTV,NetCast,HbbTV";

    /// <summary>
    /// Gets or sets la clé de signature des adresses relayées. Générée au premier démarrage ; ne pas la partager.
    /// </summary>
    public string RelaySecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets la version des réglages enregistrés, pour compléter une ancienne configuration une seule fois.
    /// 0 : avant la v0.1.3 ; 1 : « Jellyfin for Tizen » ajouté aux applications « navigateur ».
    /// </summary>
    public int ConfigVersion { get; set; }
}
