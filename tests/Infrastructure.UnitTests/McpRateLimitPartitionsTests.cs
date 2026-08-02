// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Security;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

public sealed class McpRateLimitPartitionsTests
{
    [Fact]
    public void KeyFor_WithAnAuthenticatedName_CountsTheClientUnderIt()
    {
        // Act
        var partitionKey = McpRateLimitPartitions.KeyFor("desktop-agent", matchedCertificateProfileName: null);

        // Assert
        Assert.Equal("desktop-agent", partitionKey);
    }

    [Fact]
    public void KeyFor_WithDifferentAuthenticatedNames_KeepsThemApart()
    {
        // Act
        var first = McpRateLimitPartitions.KeyFor("desktop-agent", matchedCertificateProfileName: null);
        var second = McpRateLimitPartitions.KeyFor("nightly-indexer", matchedCertificateProfileName: null);

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// A key names one client of this deployment; a profile names a client application several keys may sit behind.
    /// Taking the key keeps the partitions as narrow as the operator's key list, and refusing to combine the two is what
    /// stops one credential earning a second bucket for every profile it could present under.
    /// </summary>
    [Fact]
    public void KeyFor_WithBothIdentities_CountsTheClientUnderTheKeyAlone()
    {
        // Act
        var withCertificate = McpRateLimitPartitions.KeyFor("desktop-agent", "chatgpt-connector");
        var withoutCertificate = McpRateLimitPartitions.KeyFor("desktop-agent", matchedCertificateProfileName: null);

        // Assert
        Assert.Equal("desktop-agent", withCertificate);
        Assert.Equal(withoutCertificate, withCertificate);
    }

    [Fact]
    public void KeyFor_WithBothIdentities_GivesOneKeyNoExtraCapacityPerProfile()
    {
        // Act
        var underOneProfile = McpRateLimitPartitions.KeyFor("desktop-agent", "chatgpt-connector");
        var underAnother = McpRateLimitPartitions.KeyFor("desktop-agent", "workstation-connector");

        // Assert
        Assert.Equal(underOneProfile, underAnother);
    }

    [Fact]
    public void KeyFor_WithACertificateProfileAlone_CountsTheClientApplicationUnderIt()
    {
        // Act
        var first = McpRateLimitPartitions.KeyFor(authenticatedClientName: null, "chatgpt-connector");
        var second = McpRateLimitPartitions.KeyFor(authenticatedClientName: null, "workstation-connector");

        // Assert
        Assert.NotEqual(first, second);
        Assert.NotEqual(McpRateLimitPartitions.AnonymousKey, first);
    }

    /// <summary>
    /// Both names come from the same grammar, and under <c>ApiKey</c> both kinds of partition exist at once — a request
    /// whose credential was refused still arrives carrying the profile its certificate matched. A profile named after a
    /// key would otherwise hand that key's client the profile's capacity and the other way about.
    /// </summary>
    [Fact]
    public void KeyFor_ForAProfileNamedAfterAKey_KeepsTheTwoApart()
    {
        // Act
        var underTheKey = McpRateLimitPartitions.KeyFor("workstation", matchedCertificateProfileName: null);
        var underTheProfile = McpRateLimitPartitions.KeyFor(authenticatedClientName: null, "workstation");

        // Assert
        Assert.NotEqual(underTheKey, underTheProfile);
    }

    /// <summary>
    /// The anonymous partition holds every caller the deployment cannot tell apart, so a configured client sharing it
    /// would both spend that stream's capacity and have its own spent by it. Nothing but the spelling keeps them apart,
    /// which is why the spelling is asserted against the grammar a configured name is actually accepted under rather
    /// than left as an assumption about what an operator is likely to write.
    /// </summary>
    [Fact]
    public void AnonymousKey_CannotBeSpelledAsAConfiguredName()
    {
        // Act
        var isAcceptedAsASecretName = SecretName.TryCreate(McpRateLimitPartitions.AnonymousKey, out _);

        // Assert
        Assert.False(isAcceptedAsASecretName);
    }

    /// <summary>A profile's partition is bracketed for the same reason, so it cannot be claimed by an operator naming a key after it.</summary>
    [Fact]
    public void KeyFor_ACertificateProfilePartition_CannotBeSpelledAsAConfiguredName()
    {
        // Act
        var partitionKey = McpRateLimitPartitions.KeyFor(authenticatedClientName: null, "chatgpt-connector");

        // Assert
        Assert.False(SecretName.TryCreate(partitionKey, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void KeyFor_WithoutAnyIdentity_SharesOneAnonymousPartition(string? absentName)
    {
        // Act
        var partitionKey = McpRateLimitPartitions.KeyFor(absentName, absentName);

        // Assert
        Assert.Equal(McpRateLimitPartitions.AnonymousKey, partitionKey);
    }
}
