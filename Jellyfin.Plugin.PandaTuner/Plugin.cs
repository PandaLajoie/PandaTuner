using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using Jellyfin.Plugin.PandaTuner.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PandaTuner;

/// <summary>
/// Plugin « PandaTuner » : syntoniseur M3U capable de lecture directe (l'appareil lit la source lui-même).
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        var changed = false;

        if (string.IsNullOrEmpty(Configuration.RelaySecret))
        {
            Configuration.RelaySecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            changed = true;
        }

        // v0.1.3 : l'application Samsung (Tizen) est un navigateur de téléviseur ; on l'ajoute une seule fois aux
        // réglages existants, sans revenir dessus si l'administrateur la retire ensuite.
        if (Configuration.ConfigVersion < 1)
        {
            const string TizenApp = "Jellyfin for Tizen";
            var clients = Configuration.BrowserClients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!clients.Contains(TizenApp, StringComparer.OrdinalIgnoreCase))
            {
                Configuration.BrowserClients = string.Join(',', clients.Append(TizenApp));
            }

            Configuration.ConfigVersion = 1;
            changed = true;
        }

        if (changed)
        {
            SaveConfiguration();
        }
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "PandaTuner";

    public override Guid Id => Guid.Parse("8e7a3c52-4b1d-4f3a-9c2e-6d5b0a1f7e93");

    public override string Description =>
        "M3U tuner with direct play of channels (without going through the server). / "
        + "Syntoniseur M3U avec lecture directe des chaînes (sans passer par le serveur).";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
