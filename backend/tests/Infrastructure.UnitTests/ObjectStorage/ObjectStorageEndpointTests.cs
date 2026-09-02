// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.ObjectStorage;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers what the endpoint states explicitly so that nothing is left for the AWS client to discover.</summary>
public sealed class ObjectStorageEndpointTests
{
    private static readonly Uri Address = new("https://objects.example.test:9000/");

    [Fact]
    public void Create_AConfiguredEndpoint_CarriesEverySettingARequestIsAddressedBy()
    {
        // Act
        var endpoint = EndpointWith(keyPrefix: "mailfathom", region: "eu-central-1");

        // Assert
        Assert.Equal(Address, endpoint.Address);
        Assert.Equal("payloads", endpoint.Bucket);
        Assert.Equal("mailfathom/", endpoint.KeyPrefix);
        Assert.Equal("eu-central-1", endpoint.Region);
        Assert.True(endpoint.UsePathStyleAddressing);
        Assert.Equal(TimeSpan.FromSeconds(10), endpoint.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(100), endpoint.RequestTimeout);
    }

    /// <summary>
    /// SigV4 puts a region into the credential scope whether the endpoint has one or not, so a request always carries
    /// one. Leaving it empty is what would let the client resolve a region from the environment instead.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_AnEndpointNamingNoRegion_SignsUnderTheDefaultRatherThanNone(string configuredRegion)
    {
        // Act
        var endpoint = EndpointWith(keyPrefix: string.Empty, region: configuredRegion);

        // Assert
        Assert.Equal(ObjectStorageEndpoint.DefaultRegion, endpoint.Region);
    }

    /// <summary>Every consumer composes a key the same way, which is what keeps two deployments sharing one bucket disjoint.</summary>
    [Theory]
    [InlineData("mailfathom", "mailfathom/")]
    [InlineData("mailfathom/", "mailfathom/")]
    [InlineData("/mailfathom/", "mailfathom/")]
    [InlineData("  mailfathom/objects  ", "mailfathom/objects/")]
    [InlineData("", "")]
    public void Create_AKeyPrefix_IsNormalizedToEndInOneSeparator(string configuredPrefix, string expectedPrefix)
    {
        // Act
        var endpoint = EndpointWith(configuredPrefix, region: string.Empty);

        // Assert
        Assert.Equal(expectedPrefix, endpoint.KeyPrefix);
    }

    [Theory]
    [InlineData("mailfathom", "objects/one", "mailfathom/objects/one")]
    [InlineData("mailfathom", "/objects/one", "mailfathom/objects/one")]
    [InlineData("", "objects/one", "objects/one")]
    public void ComposeKey_ARelativeKey_IsWrittenBeneathThisDeploymentsPrefix(
        string configuredPrefix,
        string relativeKey,
        string expectedKey)
    {
        // Arrange
        var endpoint = EndpointWith(configuredPrefix, region: string.Empty);

        // Act
        var key = endpoint.ComposeKey(relativeKey);

        // Assert
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ComposeKey_ABlankRelativeKey_IsRefused(string relativeKey)
    {
        // Arrange
        var endpoint = EndpointWith(keyPrefix: string.Empty, region: string.Empty);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => endpoint.ComposeKey(relativeKey));
    }

    [Fact]
    public void ComposeKey_NoRelativeKey_IsRefused()
    {
        // Arrange
        var endpoint = EndpointWith(keyPrefix: string.Empty, region: string.Empty);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => endpoint.ComposeKey(relativeKey: null!));
    }

    [Fact]
    public void Create_AnAddressThatIsNotAbsolute_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ObjectStorageEndpoint.Create(
            new Uri("/objects", UriKind.Relative),
            "payloads",
            string.Empty,
            string.Empty,
            usePathStyleAddressing: true,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(100)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NoBucket_IsRefused(string bucket)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ObjectStorageEndpoint.Create(
            Address,
            bucket,
            string.Empty,
            string.Empty,
            usePathStyleAddressing: true,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void Create_ATimeoutThatIsNotPositive_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectStorageEndpoint.Create(
            Address,
            "payloads",
            string.Empty,
            string.Empty,
            usePathStyleAddressing: true,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(100)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectStorageEndpoint.Create(
            Address,
            "payloads",
            string.Empty,
            string.Empty,
            usePathStyleAddressing: true,
            TimeSpan.FromSeconds(10),
            TimeSpan.Zero));
    }

    private static ObjectStorageEndpoint EndpointWith(string keyPrefix, string region) => ObjectStorageEndpoint.Create(
        Address,
        " payloads ",
        keyPrefix,
        region,
        usePathStyleAddressing: true,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(100));
}
