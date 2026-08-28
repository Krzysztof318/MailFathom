// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.Basic;
using MailFathom.Host.Security.Transport;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Basic;

/// <summary>Covers what a registration must state before the scheme will judge anything.</summary>
/// <remarks>
/// Both refusals are about a registration that would compile and start: a scheme without a surface admits a request
/// carrying no identity, and one without a bound spends an unbounded number of verifications on a caller guessing.
/// Neither is something a running deployment could report, which is why the check is at registration.
/// </remarks>
public sealed class BasicAuthenticationSchemeOptionsTests
{
    [Fact]
    public void Validate_ARegistrationStatingASurfaceAndABound_IsAccepted()
    {
        // Arrange
        var options = Registered();

        // Act, Assert
        options.Validate();
    }

    [Fact]
    public void Validate_ARegistrationStatingNoSurface_IsRefused()
    {
        // Arrange
        var options = Registered();
        options.Surface = default;

        // Act, Assert
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ARegistrationStatingNoPositiveBound_IsRefused(int attemptsPerMinute)
    {
        // Arrange
        var options = Registered();
        options.AttemptsPerMinute = attemptsPerMinute;

        // Act, Assert
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static BasicAuthenticationSchemeOptions Registered() => new()
    {
        Surface = TransportSurface.Client,
        AttemptsPerMinute = 10,
    };
}
