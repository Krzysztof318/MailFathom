// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Persistence;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers what a deployment has to state before its S3-compatible endpoint may be reached at all.</summary>
public sealed class ObjectStorageOptionsTests
{
    [Fact]
    public void FindConfigurationErrors_AUsableDeclaration_ReportsNothing()
    {
        // Arrange
        var options = Usable();

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Nothing is resolved from the process environment, so an endpoint MailFathom was not told about is a startup failure.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindConfigurationErrors_NoAddress_FailsStartupNamingTheKey(string address)
    {
        // Arrange
        var options = Usable();
        options.Endpoint = address;

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("ContentStorage:ObjectStorage:Endpoint", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message is asserted rather than only the key, because the two address rules report under the same key and a
    /// value that reaches the wrong one would otherwise satisfy this test. Both inputs here are genuinely relative:
    /// a phrase carrying a space parses as no URI at all, and a host with a path and no scheme parses as a relative
    /// reference. The values that parse absolutely under the wrong scheme belong to the test below.
    /// </summary>
    [Theory]
    [InlineData("not an address")]
    [InlineData("objects.example.test/payloads")]
    public void FindConfigurationErrors_AnAddressThatIsNotAbsolute_FailsStartup(string configuredEndpoint)
    {
        // Arrange
        var options = Usable();
        options.Endpoint = configuredEndpoint;

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("ContentStorage:ObjectStorage:Endpoint", error, StringComparison.Ordinal);
        Assert.Contains("is not an absolute address", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A value that parses absolutely under some scheme other than <c>https</c> is refused by the rule that reads the
    /// scheme rather than by the one above. Both shapes here are what an operator writes by habit: a host and port with
    /// no scheme parses as an opaque URI whose scheme is the host name, and a filesystem path parses as a <c>file</c>
    /// URI on this platform.
    /// </summary>
    [Theory]
    [InlineData("objects.example.test:9000")]
    [InlineData("/objects/payloads")]
    public void FindConfigurationErrors_AnAddressThatParsesUnderAnotherScheme_FailsStartupAsPlainHttp(string configuredEndpoint)
    {
        // Arrange
        var options = Usable();
        options.Endpoint = configuredEndpoint;

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("ContentStorage:ObjectStorage:Endpoint", error, StringComparison.Ordinal);
        Assert.Contains("is not an https address", error, StringComparison.Ordinal);
    }

    /// <summary>A request to the endpoint carries a signature and, on a write, the message itself.</summary>
    [Fact]
    public void FindConfigurationErrors_APlainHttpAddress_FailsStartup()
    {
        // Arrange
        var options = Usable();
        options.Endpoint = "http://objects.example.test:9000/";

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("is not an https address", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindConfigurationErrors_NoBucket_FailsStartup(string bucket)
    {
        // Arrange
        var options = Usable();
        options.Bucket = bucket;

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("ContentStorage:ObjectStorage:Bucket", error, StringComparison.Ordinal);
    }

    /// <summary>Empty is a bucket MailFathom has to itself; whitespace is a prefix nobody can type.</summary>
    [Fact]
    public void FindConfigurationErrors_AWhitespaceKeyPrefix_FailsStartup()
    {
        // Arrange
        var options = Usable();
        options.KeyPrefix = "   ";

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("ContentStorage:ObjectStorage:KeyPrefix", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AnEmptyKeyPrefix_IsAccepted()
    {
        // Arrange
        var options = Usable();
        options.KeyPrefix = string.Empty;

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// The acceptance the whole adapter is built around, taken at the earliest point it can be: a deployment that named
    /// no credential is refused at startup rather than left to acquire the host's ambient identity at the first request.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ADeclarationMissingACredential_FailsStartupSayingNothingIsResolvedFromTheEnvironment()
    {
        // Arrange
        var withoutIdentifier = Usable();
        withoutIdentifier.AccessKeyId = null;

        var withoutSecret = Usable();
        withoutSecret.SecretAccessKey = null;

        var withAnEmptyReference = Usable();
        withAnEmptyReference.AccessKeyId = new ConfiguredSecret { Name = "object-storage-key-id" };

        // Act
        var missingIdentifier = Assert.Single(withoutIdentifier.FindConfigurationErrors());
        var missingSecret = Assert.Single(withoutSecret.FindConfigurationErrors());
        var emptyReference = Assert.Single(withAnEmptyReference.FindConfigurationErrors());

        // Assert
        Assert.Contains("ContentStorage:ObjectStorage:AccessKeyId", missingIdentifier, StringComparison.Ordinal);
        Assert.Contains("instance metadata service", missingIdentifier, StringComparison.Ordinal);
        Assert.Contains("ContentStorage:ObjectStorage:SecretAccessKey", missingSecret, StringComparison.Ordinal);
        Assert.Contains("ContentStorage:ObjectStorage:AccessKeyId", emptyReference, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ATimeoutOutsideItsRange_FailsStartupNamingTheRange()
    {
        // Arrange
        var tooShort = Usable();
        tooShort.ConnectTimeout = TimeSpan.FromMilliseconds(500);

        var tooLong = Usable();
        tooLong.RequestTimeout = TimeSpan.FromHours(1);

        // Act
        var connectError = Assert.Single(tooShort.FindConfigurationErrors());
        var requestError = Assert.Single(tooLong.FindConfigurationErrors());

        // Assert
        Assert.Contains("ContentStorage:ObjectStorage:ConnectTimeout", connectError, StringComparison.Ordinal);
        Assert.Contains("ContentStorage:ObjectStorage:RequestTimeout", requestError, StringComparison.Ordinal);
    }

    /// <summary>A whole request covers the connection it begins with, so a request budget inside the connect budget would cut every request.</summary>
    [Fact]
    public void FindConfigurationErrors_ARequestBudgetInsideTheConnectBudget_FailsStartup()
    {
        // Arrange
        var options = Usable();
        options.ConnectTimeout = TimeSpan.FromSeconds(30);
        options.RequestTimeout = TimeSpan.FromSeconds(30);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.Contains("ContentStorage:ObjectStorage:RequestTimeout", error, StringComparison.Ordinal);
        Assert.Contains("ConnectTimeout", error, StringComparison.Ordinal);
    }

    /// <summary>A self-hosted endpoint reached by address has no wildcard DNS name and no certificate to match one.</summary>
    [Fact]
    public void ToEndpoint_ADeploymentThatConfiguresNothingFurther_AddressesTheBucketInThePath()
    {
        // Arrange
        var options = Usable();

        // Act
        var endpoint = options.ToEndpoint();

        // Assert
        Assert.True(endpoint.UsePathStyleAddressing);
        Assert.Equal(TimeSpan.FromSeconds(10), endpoint.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(100), endpoint.RequestTimeout);
        Assert.Equal(ObjectStorageEndpoint.DefaultRegion, endpoint.Region);
    }

    [Fact]
    public void ToEndpoint_AConfiguredDeclaration_CarriesEverySettingARequestIsAddressedBy()
    {
        // Arrange
        var options = Usable();
        options.Endpoint = " https://objects.example.test:9000/ ";
        options.KeyPrefix = "mailfathom";
        options.Region = "eu-central-1";
        options.UsePathStyleAddressing = false;
        options.ConnectTimeout = TimeSpan.FromSeconds(5);
        options.RequestTimeout = TimeSpan.FromSeconds(30);

        // Act
        var endpoint = options.ToEndpoint();

        // Assert
        Assert.Equal(new Uri("https://objects.example.test:9000/"), endpoint.Address);
        Assert.Equal("payloads", endpoint.Bucket);
        Assert.Equal("mailfathom/", endpoint.KeyPrefix);
        Assert.Equal("eu-central-1", endpoint.Region);
        Assert.False(endpoint.UsePathStyleAddressing);
        Assert.Equal(TimeSpan.FromSeconds(5), endpoint.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), endpoint.RequestTimeout);
    }

    private static ObjectStorageOptions Usable() => ContentStorageOptionsTests.UsableEndpoint();
}
