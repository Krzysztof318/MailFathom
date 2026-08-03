// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers how the command settles which deployment it is about to send a credential to.</summary>
/// <remarks>
/// This decides where a bearer credential is sent, so guessing is not an option anywhere in it. Every input that does
/// not name an address unambiguously is refused with something the operator can act on.
/// </remarks>
public sealed class CliOptionsTests
{
    [Fact]
    public void ResolveEndpoint_AnAddressOnTheCommandLine_IsWhatTheCommandReaches()
    {
        // Act
        var endpoint = CliOptions.ResolveEndpoint("https://mail.example.test:8443");

        // Assert
        Assert.Equal("https://mail.example.test:8443/", endpoint.ToString());
    }

    [Fact]
    public void ResolveEndpoint_NothingAnywhere_SaysBothWaysToSupplyIt()
    {
        // Act
        var failure = Assert.Throws<CliFailure>(() => CliOptions.ResolveEndpoint(configuredEndpoint: null));

        // Assert
        Assert.Contains("--endpoint", failure.Message, StringComparison.Ordinal);
        Assert.Contains(CliOptions.EndpointVariable, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bare host name is refused rather than completed with a scheme. Choosing one would decide between a protected
    /// and an unprotected transport for a request that carries a credential, which is not a default to pick for someone.
    /// </summary>
    [Theory]
    [InlineData("mail.example.test:8443")]
    [InlineData("ftp://mail.example.test")]
    [InlineData("/api")]
    [InlineData("   ")]
    public void ResolveEndpoint_SomethingThatIsNotAnHttpAddress_IsRefused(string candidate)
    {
        // Act, Assert
        Assert.Throws<CliFailure>(() => CliOptions.ResolveEndpoint(candidate));
    }

    [Fact]
    public void ResolveEndpoint_APlainHttpAddress_IsAccepted()
    {
        // Act, Assert: refusing it would leave a loopback deployment and a reverse proxy unreachable, and the endpoint
        // warns about clear text on its own side where it knows whether anything is in front of it.
        Assert.Equal("http", CliOptions.ResolveEndpoint("http://localhost:8090").Scheme);
    }
}
