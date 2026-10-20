using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.PandaTuner;

/// <summary>
/// Flux « ouvert » pour le mode proxy, équivalent du LiveStream de Jellyfin 12.0 pour une source HLS :
/// rien à télécharger ici, ffmpeg lit l'adresse d'origine lui-même. L'ouverture sert surtout à ce que Jellyfin
/// sonde le flux (vraies pistes, codecs) avant de choisir entre recopie et réencodage.
/// </summary>
public sealed class PandaLiveStream : ILiveStream
{
    public PandaLiveStream(MediaSourceInfo mediaSource, string tunerHostId)
    {
        MediaSource = mediaSource;
        TunerHostId = tunerHostId;
        UniqueId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        ConsumerCount = 1;
    }

    public int ConsumerCount { get; set; }

    public string OriginalStreamId { get; set; } = string.Empty;

    public string TunerHostId { get; }

    public bool EnableStreamSharing { get; private set; } = true;

    public MediaSourceInfo MediaSource { get; set; }

    public string UniqueId { get; }

    public Task Open(CancellationToken openCancellationToken) => Task.CompletedTask;

    public Task Close()
    {
        EnableStreamSharing = false;
        return Task.CompletedTask;
    }

    // Seulement utilisé par les flux copiés dans un fichier temporaire (SharedHttpStream) : pas notre cas.
    public Stream GetStream() => throw new NotSupportedException("Le flux est lu directement par ffmpeg depuis l'adresse d'origine.");

    public void Dispose()
    {
    }
}
