// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

/// <summary>Covers the shape of the verdict itself, and the revision that says which list produced one.</summary>
public sealed class SenderTrustTests
{
    /// <summary>A reading no policy has spoken over claims nothing and says that no policy reached it.</summary>
    [Fact]
    public void NotEvaluated_BeforeAPolicyRuns_IsUnknownAndNamesNoPolicy()
    {
        // Act
        var trust = SenderTrust.NotEvaluated;

        // Assert
        Assert.False(trust.IsTrusted);
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
        Assert.False(trust.PolicyRevision.NamesAPolicy);
    }

    /// <summary>A verdict that recognized an author says what recognized them, so it cannot be reached by nothing.</summary>
    [Fact]
    public void Trusted_WithoutNamingWhatRecognizedTheAuthor_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => SenderTrust.Trusted(
            SenderTrustSource.None,
            SenderTrustPolicyRevision.None));

        // Assert
        Assert.Equal("grantedBy", refusal.ParamName);
    }

    /// <summary>The same list always names itself the same way, including in another process.</summary>
    [Fact]
    public void Of_TheSameStatements_ProducesTheSameRevision()
    {
        // Act
        var first = SenderTrustPolicyRevision.Of(["domain:A.EXAMPLE", "domain:B.EXAMPLE"]);
        var second = SenderTrustPolicyRevision.Of(["domain:B.EXAMPLE", "domain:A.EXAMPLE"]);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(SenderTrustPolicyRevision.Length, first.Value.Length);
        Assert.Equal(first.Value, first.ToString());
    }

    /// <summary>A stored revision is opaque, so reading one back neither re-derives nor re-checks it.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    public void FromStoredValue_WhatAColumnHeld_NamesAPolicyOnlyWhenTheColumnDid(string? stored, bool expected)
    {
        // Act
        var revision = SenderTrustPolicyRevision.FromStoredValue(stored);

        // Assert
        Assert.Equal(expected, revision.NamesAPolicy);
    }
}
