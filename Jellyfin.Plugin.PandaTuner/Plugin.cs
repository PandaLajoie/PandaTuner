using System;
using System.Collections.Generic;
using System.Globalization;
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

        if (string.IsNullOrEmpty(Configuration.RelaySecret))
        {
            Configuration.RelaySecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            SaveConfiguration();
        }
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "PandaTuner";

    public override Guid Id => Guid.Parse("8e7a3c52-4b1d-4f3a-9c2e-6d5b0a1f7e93");

    public override string Description => "Syntoniseur M3U avec lecture directe des chaînes (sans passer par le serveur).";

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
