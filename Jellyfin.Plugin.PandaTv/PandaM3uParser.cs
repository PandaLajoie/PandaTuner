using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;

namespace Jellyfin.Plugin.PandaTv;

/// <summary>
/// Une chaîne lue dans la liste, avec son mode de diffusion.
/// </summary>
public sealed class PandaChannel
{
    public required ChannelInfo Info { get; init; }

    public required string Mode { get; init; }
}

/// <summary>
/// Lecteur M3U : mêmes règles que le M3uParser de Jellyfin 12.0 (tvg-id, tvg-chno, tvg-logo, group-title),
/// plus l'attribut <c>panda-mode="direct|proxy"</c>.
/// </summary>
public static partial class PandaM3uParser
{
    private const string ExtInfPrefix = "#EXTINF:";

    public static async Task<string> ReadPlaylistAsync(TunerHostInfo tuner, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var url = tuner.Url;
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return await httpClient.GetStringAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        }

        return await File.ReadAllTextAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public static List<PandaChannel> Parse(string content, string tunerHostId, string channelIdPrefix, string defaultMode)
    {
        var channels = new List<PandaChannel>();
        string? extInf = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith(ExtInfPrefix, StringComparison.OrdinalIgnoreCase))
            {
                extInf = line[ExtInfPrefix.Length..].Trim();
            }
            else if (extInf is not null && !line.StartsWith('#'))
            {
                if (Uri.TryCreate(line, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    channels.Add(CreateChannel(extInf, line, tunerHostId, channelIdPrefix, defaultMode));
                }

                extInf = null;
            }
        }

        return channels;
    }

    private static PandaChannel CreateChannel(string extInf, string url, string tunerHostId, string channelIdPrefix, string defaultMode)
    {
        var attributes = ParseAttributes(extInf);

        attributes.TryGetValue("tvg-id", out var tvgId);
        attributes.TryGetValue("channel-id", out var channelId);
        if (!attributes.TryGetValue("tvg-logo", out var logo))
        {
            attributes.TryGetValue("logo", out logo);
        }

        attributes.TryGetValue("group-title", out var group);
        attributes.TryGetValue("panda-mode", out var mode);

        var info = new ChannelInfo
        {
            Id = channelIdPrefix + Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant(),
            TunerHostId = tunerHostId,
            Path = url,
            Name = GetName(extInf, attributes),
            Number = GetNumber(attributes),
            TunerChannelId = string.IsNullOrWhiteSpace(tvgId) ? channelId : tvgId,
            ImageUrl = string.IsNullOrWhiteSpace(logo) ? null : logo,
            ChannelGroup = group,
            ChannelType = ChannelType.TV
        };

        return new PandaChannel { Info = info, Mode = StreamMode.Normalize(mode, defaultMode) };
    }

    private static string? GetNumber(Dictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("tvg-chno", out var chno)
            && double.TryParse(chno, CultureInfo.InvariantCulture, out var value)
            && value > 0)
        {
            return chno.Trim();
        }

        return null;
    }

    private static string? GetName(string extInf, Dictionary<string, string> attributes)
    {
        // Le nom est après la dernière virgule hors attributs : on retire d'abord les attributs (ils peuvent contenir des virgules).
        var remaining = AttributeRegex().Replace(extInf, string.Empty);
        var commaIndex = remaining.LastIndexOf(',');
        var name = commaIndex >= 0 ? remaining[(commaIndex + 1)..].Trim() : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            attributes.TryGetValue("tvg-name", out name);
        }

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static Dictionary<string, string> ParseAttributes(string extInf)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex().Matches(extInf))
        {
            dict[match.Groups[1].Value] = match.Groups[2].Value;
        }

        return dict;
    }

    [GeneratedRegex("([a-z0-9\\-_]+)=\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();
}
