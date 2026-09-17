using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace DevPane.Integrations.Web;

/// <summary>
/// Finds a website's icon the way browsers do: the icons its home page declares with &lt;link rel="icon"&gt;, then
/// /favicon.ico. Results are cached, so each site is checked at most a few times a day.
/// </summary>
public static partial class SiteIcons
{
    private const int MaxPageBytes = 256 * 1024;
    private const int MaxInlineSvgBytes = 32 * 1024;

    private static readonly TimeSpan FoundLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan MissingLifetime = TimeSpan.FromHours(1);

    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, (DateTimeOffset CheckedAt, string? Icon)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// An image URL for the site's icon, or null if it has none that loads. SVG icons come back as data URIs, since those
    /// are the form of SVG the Widgets Board is known to draw.
    /// </summary>
    /// <param name="domain">A host name such as "keiru.dev".</param>
    public static async Task<string?> FindAsync(string domain, CancellationToken cancellationToken)
    {
        if (Cache.TryGetValue(domain, out var cached)
            && DateTimeOffset.Now - cached.CheckedAt < (cached.Icon is null ? MissingLifetime : FoundLifetime))
        {
            return cached.Icon;
        }

        string? icon = null;
        try
        {
            var site = new Uri($"https://{domain}/");
            foreach (var candidate in await CandidatesAsync(site, cancellationToken))
            {
                icon = await LoadAsync(candidate, cancellationToken);
                if (icon is not null)
                {
                    break;
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            // Unreachable or protected sites simply have no icon.
        }

        Cache[domain] = (DateTimeOffset.Now, icon);
        return icon;
    }

    // Icons in order of how well they draw on a card: PNG, then SVG, then ICO, then /favicon.ico.
    private static async Task<List<Uri>> CandidatesAsync(Uri site, CancellationToken cancellationToken)
    {
        var declared = new List<(Uri Url, int Rank, int Size)>();
        using (var response = await Http.GetAsync(site, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/html")
            {
                string html = await ReadTextAsync(response, MaxPageBytes, cancellationToken);
                var baseUrl = response.RequestMessage?.RequestUri ?? site;
                foreach (Match tag in LinkTag().Matches(html))
                {
                    var attributes = Attributes(tag.Value);
                    string rel = attributes.GetValueOrDefault("rel", string.Empty).ToLowerInvariant();
                    if (!rel.Split(' ').Any(part => part is "icon" or "apple-touch-icon")
                        || !attributes.TryGetValue("href", out string? href)
                        || !Uri.TryCreate(baseUrl, WebUtility.HtmlDecode(href), out var url)
                        || url.Scheme != Uri.UriSchemeHttps)
                    {
                        continue;
                    }

                    string type = attributes.GetValueOrDefault("type", string.Empty).ToLowerInvariant();
                    string path = url.AbsolutePath.ToLowerInvariant();
                    int rank = type == "image/png" || path.EndsWith(".png", StringComparison.Ordinal) ? 0
                        : type == "image/svg+xml" || path.EndsWith(".svg", StringComparison.Ordinal) ? 1
                        : 2;
                    declared.Add((url, rank, LargestSize(attributes.GetValueOrDefault("sizes", string.Empty))));
                }
            }
        }

        // Among PNGs, the smallest one at least 64px wide looks sharpest without being large.
        var candidates = declared
            .OrderBy(icon => icon.Rank)
            .ThenBy(icon => icon.Size >= 64 ? icon.Size : 10_000 - icon.Size)
            .Select(icon => icon.Url)
            .Take(3)
            .ToList();
        candidates.Add(new Uri(site, "/favicon.ico"));
        return candidates;
    }

    private static async Task<string?> LoadAsync(Uri url, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!response.IsSuccessStatusCode || mediaType is null || !mediaType.StartsWith("image/", StringComparison.Ordinal))
        {
            return null;
        }

        if (mediaType != "image/svg+xml")
        {
            return url.AbsoluteUri;
        }

        string svg = await ReadTextAsync(response, MaxInlineSvgBytes + 1, cancellationToken);
        return Encoding.UTF8.GetByteCount(svg) <= MaxInlineSvgBytes
            ? "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))
            : null;
    }

    private static async Task<string> ReadTextAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes];
        int total = 0;
        int read;
        while (total < maxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), cancellationToken)) > 0)
        {
            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static Dictionary<string, string> Attributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in Attribute().Matches(tag))
        {
            string value = attribute.Groups["double"].Success ? attribute.Groups["double"].Value
                : attribute.Groups["single"].Success ? attribute.Groups["single"].Value
                : attribute.Groups["bare"].Value;
            attributes.TryAdd(attribute.Groups["name"].Value, value);
        }

        return attributes;
    }

    // "sizes" looks like "96x96", "16x16 32x32", or "any".
    private static int LargestSize(string sizes) =>
        sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(size => size.Split('x', 'X')[0])
            .Select(width => int.TryParse(width, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(8),
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DevPane", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"(?<name>[\w:-]+)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s>]+))")]
    private static partial Regex Attribute();
}
