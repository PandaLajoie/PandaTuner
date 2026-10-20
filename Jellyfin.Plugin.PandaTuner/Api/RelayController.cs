using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PandaTuner.Api;

/// <summary>
/// Sert les playlists relayées (voir <see cref="PlaylistRelay"/>). Sans authentification : les lecteurs HLS
/// (hls.js, lecteur du téléviseur) n'envoient pas le jeton Jellyfin. Pour ne pas servir de proxy ouvert,
/// la playlist principale n'accepte qu'une chaîne connue, et les autres adresses doivent être signées par le serveur.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(PlaylistRelay.RoutePrefix + "/{channelId}")]
public class RelayController : ControllerBase
{
    private const string PlaylistContentType = "application/vnd.apple.mpegurl";

    private readonly PandaM3uTunerHost _tunerHost;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RelayController> _logger;

    public RelayController(PandaM3uTunerHost tunerHost, IHttpClientFactory httpClientFactory, ILogger<RelayController> logger)
    {
        _tunerHost = tunerHost;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("master.m3u8")]
    public async Task<ActionResult> GetMaster([FromRoute] string channelId, CancellationToken cancellationToken)
    {
        var channel = await _tunerHost.FindChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel?.Info.Path is null)
        {
            return NotFound();
        }

        return await RelayPlaylist(channel.Info.Path, cancellationToken).ConfigureAwait(false);
    }

    [HttpGet("p.m3u8")]
    public async Task<ActionResult> GetPlaylist([FromQuery] string u, [FromQuery] string s, CancellationToken cancellationToken)
    {
        var url = DecodeVerified(u, s);
        return url is null ? Forbid() : await RelayPlaylist(url, cancellationToken).ConfigureAwait(false);
    }

    [HttpGet("k")]
    public async Task<ActionResult> GetKey([FromQuery] string u, [FromQuery] string s, CancellationToken cancellationToken)
    {
        var url = DecodeVerified(u, s);
        if (url is null)
        {
            return Forbid();
        }

        using var response = await Fetch(url, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return File(bytes, "application/octet-stream");
    }

    private async Task<ActionResult> RelayPlaylist(string url, CancellationToken cancellationToken)
    {
        using var response = await Fetch(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Relais : {Url} a répondu {Status}", url, (int)response.StatusCode);
            return StatusCode((int)response.StatusCode);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        // Adresse finale après redirections (ex. jmp2.uk → serveur Pluto) : base des adresses relatives.
        var finalUri = response.RequestMessage?.RequestUri ?? new Uri(url);
        var rewritten = PlaylistRelay.Rewrite(content, finalUri, PandaM3uTunerHost.RelaySecret);

        Response.Headers.CacheControl = "no-cache";
        return Content(rewritten, PlaylistContentType);
    }

    private async Task<HttpResponseMessage> Fetch(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", PandaM3uTunerHost.BrowserUserAgent);
        return await _httpClientFactory.CreateClient(NamedClient.Default)
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? DecodeVerified(string? encoded, string? signature)
    {
        if (string.IsNullOrEmpty(encoded) || string.IsNullOrEmpty(signature))
        {
            return null;
        }

        try
        {
            var url = PlaylistRelay.DecodeUrl(encoded);
            return PlaylistRelay.Verify(url, signature, PandaM3uTunerHost.RelaySecret) ? url : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
