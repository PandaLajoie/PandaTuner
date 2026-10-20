using MediaBrowser.Controller;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PandaTuner;

/// <summary>
/// Déclare le syntoniseur du plugin : Jellyfin récupère tous les <see cref="ITunerHost"/> enregistrés.
/// Il est aussi enregistré sous son propre type, pour que le relais de playlists retrouve les chaînes.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<PandaM3uTunerHost>();
        serviceCollection.AddSingleton<ITunerHost>(sp => sp.GetRequiredService<PandaM3uTunerHost>());
    }
}
