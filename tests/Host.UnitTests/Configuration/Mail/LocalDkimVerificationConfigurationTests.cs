// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Mail;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers the one switch deciding whether this deployment verifies a sender for itself.</summary>
public sealed class LocalDkimVerificationConfigurationTests
{
    /// <summary>It defaults to on, because the deployment it exists for is one that configured nothing.</summary>
    /// <remarks>
    /// A mailbox whose receiving server writes no <c>Authentication-Results</c> header records that nothing was
    /// established on every message it holds. Shipping the fallback off would switch it off for exactly those
    /// mailboxes while leaving them looking correctly configured.
    /// </remarks>
    [Fact]
    public void VerifyDkimLocally_AnUnconfiguredDeployment_VerifiesForItself()
    {
        // Arrange
        var options = new MailSynchronizationOptions();

        // Act, Assert
        Assert.True(options.VerifyDkimLocally);
    }

    /// <summary>An operator who wants no egress from the extraction path turns it off, and the binder carries that.</summary>
    [Fact]
    public void VerifyDkimLocally_TurnedOffInConfiguration_IsBoundAsOff()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:VerifyDkimLocally"] = "false",
            })
            .Build();

        // Act
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>();

        // Assert
        Assert.NotNull(options);
        Assert.False(options.VerifyDkimLocally);
    }
}
