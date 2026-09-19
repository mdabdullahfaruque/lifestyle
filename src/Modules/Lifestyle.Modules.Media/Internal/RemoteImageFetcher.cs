using System.Net;
using System.Net.Sockets;
using Lifestyle.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Media.Internal;

/// <summary>
/// Fetches an image the seller named by URL, for the bulk-import <c>image_urls</c> column
/// (docs/08 §7.2).
/// <para>
/// <b>This is server-side request forgery by design</b>: the seller chooses what our server
/// connects to. On this box that means the loopback interface, the Docker bridge, the cloud
/// metadata endpoint, and four other products' containers. Every guard below exists because of
/// that, and the feature is <b>off unless <c>Media:RemoteImageImport:Enabled</c> is set</b> — the
/// image library solves the same seller problem with none of this risk, so this should stay off
/// unless someone has a reason.
/// </para>
/// </summary>
internal sealed class RemoteImageFetcher(HttpClient http, IOptions<MediaModuleOptions> options)
{
    private readonly MediaModuleOptions _options = options.Value;

    /// <summary>Redirects are followed by hand so each hop is re-validated, never blindly.</summary>
    private const int MaxRedirects = 3;

    public async Task<Result<InMemoryFormFile>> FetchAsync(string url, CancellationToken ct)
    {
        var settings = _options.RemoteImageImport;

        if (!settings.Enabled)
            return Error.Validation("media.url_import_disabled",
                "Importing images by URL is switched off. Upload them to your image library instead.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Error.Validation("media.url_invalid", $"'{url}' is not a valid web address.");

        var current = uri;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            var allowed = await IsPubliclyRoutableAsync(current, ct);
            if (allowed.IsFailure) return allowed.Error;

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                return Error.Validation("media.url_unreachable", $"Could not download '{url}'.");
            }

            using (response)
            {
                // Redirects are not followed by HttpClient (AllowAutoRedirect is off) precisely so
                // the destination of each hop goes back through the address check. A URL on a
                // public host that redirects to 169.254.169.254 is the classic bypass.
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location;
                    if (location is null)
                        return Error.Validation("media.url_unreachable", $"Could not download '{url}'.");

                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    return Error.Validation("media.url_unreachable",
                        $"'{url}' returned {(int)response.StatusCode}.");

                // Content-Length is a hint the remote host controls, so it is a cheap early reject
                // and never the actual limit — the read below is what enforces it.
                if (response.Content.Headers.ContentLength is { } declared && declared > settings.MaxBytes)
                    return TooLarge(settings.MaxBytes);

                var body = await ReadCappedAsync(response, settings.MaxBytes, timeout.Token);
                if (body is null) return TooLarge(settings.MaxBytes);

                // The content type is asserted by a host the seller chose. It decides only the
                // file extension here; UploadFile decodes the bytes and is what actually proves
                // this is an image.
                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                var fileName = FileNameFor(uri, contentType);

                return new InMemoryFormFile(body, fileName, contentType ?? "image/jpeg");
            }
        }

        return Error.Validation("media.url_too_many_redirects", $"'{url}' redirected too many times.");
    }

    private static Error TooLarge(long maxBytes) =>
        Error.Validation("media.url_too_large",
            $"That image is larger than the {maxBytes / (1024 * 1024)} MB limit.");

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/>, returning null the moment it is exceeded, so a
    /// host that streams forever cannot exhaust memory.
    /// </summary>
    private static async Task<MemoryStream?> ReadCappedAsync(
        HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new MemoryStream();
        var chunk = new byte[81920];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) break;

            if (buffer.Length + read > maxBytes)
            {
                await buffer.DisposeAsync();
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// Resolves the host and refuses anything that is not a public address.
    /// <para>
    /// Resolution happens here, before connecting, because the check has to be on the address the
    /// request will actually reach — a hostname is not evidence of anything. Every address a name
    /// resolves to must pass, not merely the first, or a name with one public and one private
    /// record walks straight through.
    /// </para>
    /// </summary>
    private async Task<Result> IsPubliclyRoutableAsync(Uri uri, CancellationToken ct)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return Error.Validation("media.url_scheme", "Only http and https addresses can be imported.");

        if (!_options.RemoteImageImport.AllowInsecureHttp && uri.Scheme != Uri.UriSchemeHttps)
            return Error.Validation("media.url_scheme", "Only https addresses can be imported.");

        IPAddress[] addresses;

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            }
            catch (SocketException)
            {
                return Error.Validation("media.url_unreachable", $"Could not resolve '{uri.Host}'.");
            }
        }

        if (addresses.Length == 0)
            return Error.Validation("media.url_unreachable", $"Could not resolve '{uri.Host}'.");

        return Array.Exists(addresses, a => !IsPublic(a))
            ? Error.Validation("media.url_not_public",
                "That address is on a private network and cannot be imported.")
            : Result.Success();
    }

    /// <summary>
    /// True only for addresses that are routable on the public internet.
    /// <para>
    /// An allow-nothing-suspicious list rather than a block list of known-bad addresses: the cloud
    /// metadata endpoint is only the most famous target, and a block list would have to enumerate
    /// every private range of two address families correctly to be worth anything.
    /// </para>
    /// </summary>
    internal static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // A v4 address wearing a v6 costume reaches exactly the same host.
            if (address.IsIPv4MappedToIPv6) return IsPublic(address.MapToIPv4());

            return !address.IsIPv6LinkLocal
                   && !address.IsIPv6SiteLocal
                   && !address.IsIPv6Multicast
                   && !IsUniqueLocalV6(address)
                   && !address.Equals(IPAddress.IPv6Any);
        }

        if (address.AddressFamily != AddressFamily.InterNetwork) return false;

        var octets = address.GetAddressBytes();

        return octets[0] switch
        {
            0 => false,                                  // "this network"
            10 => false,                                 // private
            127 => false,                                // loopback
            169 when octets[1] == 254 => false,           // link-local, incl. cloud metadata
            172 when octets[1] >= 16 && octets[1] <= 31 => false, // private
            192 when octets[1] == 168 => false,           // private
            100 when octets[1] >= 64 && octets[1] <= 127 => false, // carrier-grade NAT
            192 when octets[1] == 0 && octets[2] == 0 => false,    // IETF protocol assignments
            198 when octets[1] == 18 || octets[1] == 19 => false,  // benchmarking
            >= 224 => false,                             // multicast and reserved
            _ => true
        };
    }

    /// <summary>fc00::/7 — the v6 equivalent of 10.0.0.0/8.</summary>
    private static bool IsUniqueLocalV6(IPAddress address) =>
        (address.GetAddressBytes()[0] & 0xFE) == 0xFC;

    private static string FileNameFor(Uri uri, string? contentType)
    {
        var fromPath = Path.GetFileName(uri.AbsolutePath);

        var cleaned = new string([.. fromPath.Where(c => !Path.GetInvalidFileNameChars().Contains(c))]);

        if (!string.IsNullOrWhiteSpace(cleaned) && Path.HasExtension(cleaned))
            return cleaned.Length > 255 ? cleaned[..255] : cleaned;

        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        // The name is what the import matcher reads, so a URL with no usable filename still gets
        // something stable rather than a random id it could never match on.
        return string.IsNullOrWhiteSpace(cleaned) ? $"image{extension}" : $"{cleaned}{extension}";
    }
}
