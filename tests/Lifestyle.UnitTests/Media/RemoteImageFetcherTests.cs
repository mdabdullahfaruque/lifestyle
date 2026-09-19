using System.Net;
using Lifestyle.Modules.Media.Internal;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.Media;

/// <summary>
/// The address check is the whole SSRF defence for <c>image_urls</c> (docs/08 §7.2). The seller
/// chooses the URL, and this box shares a host with four other products and a cloud metadata
/// endpoint, so anything this lets through is something a vendor can make our server talk to.
/// </summary>
public sealed class RemoteImageFetcherTests
{
    [Theory]
    // Loopback — our own API, and the other products' containers on 127.0.0.1:5X00.
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("0.0.0.0")]
    // Cloud metadata. The single most-targeted SSRF destination there is.
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.1")]
    // RFC 1918 — the Docker bridge and anything else on the host network.
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    // Carrier-grade NAT, protocol assignments, benchmarking, multicast.
    [InlineData("100.64.0.1")]
    [InlineData("192.0.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    public void Private_and_reserved_v4_addresses_are_refused(string address)
    {
        RemoteImageFetcher.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("ff02::1")]
    [InlineData("::")]
    public void Private_and_reserved_v6_addresses_are_refused(string address)
    {
        RemoteImageFetcher.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();
    }

    /// <summary>
    /// A v4 address wearing a v6 costume reaches exactly the same host, and is a standard way past
    /// a check that only understands one family.
    /// </summary>
    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:192.168.0.1")]
    public void A_v4_address_mapped_into_v6_is_refused_too(string address)
    {
        RemoteImageFetcher.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]   // just outside the private 172.16/12 range
    [InlineData("192.169.0.1")]  // just outside 192.168/16
    [InlineData("100.128.0.1")]  // just outside the CGNAT range
    [InlineData("2606:4700::1")]
    public void Genuinely_public_addresses_are_allowed(string address)
    {
        RemoteImageFetcher.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();
    }
}
