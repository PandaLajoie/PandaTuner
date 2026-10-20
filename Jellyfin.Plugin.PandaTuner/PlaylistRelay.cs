using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.PandaTuner;

/// <summary>
/// Relais de playlists HLS : le serveur ne transmet que les playlists (texte, quelques Ko), en réécrivant
/// les adresses des segments vers la source. La vidéo va directement de la source à l'appareil.
/// Utile quand la source refuse les navigateurs d'autres sites (CORS, ex. Pluto) ou utilise des adresses relatives.
/// </summary>
public static partial class PlaylistRelay
{
    /// <summary>Route de base des playlists relayées (contrôleur <see cref="Api.RelayController"/>).</summary>
    public const string RoutePrefix = "PandaTuner/Hls";

    /// <summary>
    /// Réécrit une playlist récupérée à <paramref name="playlistUri"/> (adresse finale, après redirections).
    /// Sous-playlists et clés → relayées (adresses relatives au relais, signées) ; segments → adresses complètes vers la source.
    /// </summary>
    public static string Rewrite(string content, Uri playlistUri, string secret)
    {
        var isMaster = content.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal)
            || content.Contains("#EXT-X-MEDIA:", StringComparison.Ordinal);

        var output = new StringBuilder(content.Length + 512);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                output.Append('\n');
            }
            else if (line[0] != '#')
            {
                // Ligne d'adresse : sous-playlist dans une playlist principale, segment sinon.
                var absolute = new Uri(playlistUri, line.Trim());
                output.Append(isMaster ? RelayLink("p.m3u8", absolute, secret) : absolute.AbsoluteUri).Append('\n');
            }
            else
            {
                output.Append(RewriteTagUri(line, playlistUri, secret)).Append('\n');
            }
        }

        return output.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>Signature HMAC d'une adresse de la source, pour que le relais ne serve que ce qu'il a lui-même produit.</summary>
    public static string Sign(string url, string secret)
        => Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(url)));

    public static bool Verify(string url, string signature, string secret)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sign(url, secret)),
            Encoding.ASCII.GetBytes(signature ?? string.Empty));

    public static string EncodeUrl(string url) => Base64Url(Encoding.UTF8.GetBytes(url));

    public static string DecodeUrl(string encoded)
    {
        var s = encoded.Replace('-', '+').Replace('_', '/');
        s += new string('=', (4 - (s.Length % 4)) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    private static string RewriteTagUri(string line, Uri playlistUri, string secret)
    {
        // Balises avec URI="…" : EXT-X-MEDIA et EXT-X-I-FRAME-STREAM-INF (sous-playlists), EXT-X-KEY (clé),
        // EXT-X-MAP (segment d'initialisation, envoyé en direct comme les autres segments).
        var relayTarget = line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal)
                || line.StartsWith("#EXT-X-I-FRAME-STREAM-INF:", StringComparison.Ordinal)
            ? "p.m3u8"
            : line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal) || line.StartsWith("#EXT-X-SESSION-KEY:", StringComparison.Ordinal)
                ? "k"
                : null;

        return UriAttributeRegex().Replace(line, match =>
        {
            var absolute = new Uri(playlistUri, match.Groups[1].Value);
            var value = relayTarget is null ? absolute.AbsoluteUri : RelayLink(relayTarget, absolute, secret);
            return "URI=\"" + value + "\"";
        });
    }

    private static string RelayLink(string target, Uri absolute, string secret)
    {
        var url = absolute.AbsoluteUri;
        return target + "?u=" + EncodeUrl(url) + "&s=" + Sign(url, secret);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex("URI=\"([^\"]*)\"")]
    private static partial Regex UriAttributeRegex();
}
