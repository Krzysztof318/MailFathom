// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Owners;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Owners;

/// <summary>
/// Covers what an owner's record has to satisfy before a statement is issued for it. The bound is the one the read
/// enforces from the other direction, so what matters here is that the two measure the same thing: a record accepted on
/// its compact length and stored larger than that is a row the next start refuses on a change that was reported as
/// committed.
/// </summary>
public sealed class OwnerSettingsCommitRulesTests
{
    /// <summary>A record is a document of settings, and a root that is not an object carries none of them.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("\"a record\"")]
    [InlineData("7")]
    [InlineData("null")]
    public void RefuseWhatCannotBeCommitted_JsonWhoseRootIsNotAnObject_IsRefused(string json)
    {
        // Act & Assert
        var refusal = Assert.Throws<ArgumentException>(
            () => OwnerSettingsCommitRules.RefuseWhatCannotBeCommitted(json, expectedVersion: 1));

        Assert.Contains("not an object", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The column's own cast would refuse it, and the refusal belongs on the side that composed the document — so it is
    /// an argument the build got wrong rather than a fault the database reports.
    /// </summary>
    [Fact]
    public void RefuseWhatCannotBeCommitted_ACandidateThatIsNotJsonAtAll_IsRefusedBeforeAnyStatement()
    {
        // Act & Assert
        var refusal = Assert.Throws<ArgumentException>(
            () => OwnerSettingsCommitRules.RefuseWhatCannotBeCommitted("{\"MailAccounts\":", expectedVersion: 1));

        Assert.Contains("not JSON", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The database stores a rendering of its own, with separators the compact form has none of, so a record that fits
    /// compacted can be stored past what the read binds from — which is a row committed and then unreadable.
    /// </summary>
    [Fact]
    public void RefuseWhatCannotBeCommitted_ARecordThatOnlyFitsCompacted_IsRefused()
    {
        // Arrange
        var properties = Enumerable.Range(0, 74_000).Select(position => $"\"k{position}\":\"v\"");
        var compact = $"{{{string.Join(',', properties)}}}";

        // Act & Assert
        Assert.True(
            compact.Length <= OwnerSettingsDocument.MaximumOctets,
            "The record has to fit compacted for the claim to mean anything.");

        var refusal = Assert.Throws<ArgumentException>(
            () => OwnerSettingsCommitRules.RefuseWhatCannotBeCommitted(compact, expectedVersion: 1));

        Assert.Contains("octets", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A version is what the change was composed over, and there is no such version below zero.</summary>
    [Fact]
    public void RefuseWhatCannotBeCommitted_ANegativeVersion_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OwnerSettingsCommitRules.RefuseWhatCannotBeCommitted("{}", expectedVersion: -1));
    }

    /// <summary>A statement carrying no document at all is a candidate this build never composed, rather than an empty record.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RefuseWhatCannotBeCommitted_NoCandidateAtAll_IsRefused(string? json)
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => OwnerSettingsCommitRules.RefuseWhatCannotBeCommitted(json!, expectedVersion: 1));
    }

    /// <summary>A record the next read binds is handed on rather than refused, which is what keeps the rules above from refusing everything.</summary>
    [Fact]
    public void RefuseWhatCannotBeCommitted_ARecordTheNextReadWouldBind_IsHandedOn()
    {
        // Act & Assert
        OwnerSettingsCommitRules.RefuseWhatCannotBeCommitted(
            """{"MailAccounts":[{"AccountId":"work"}]}""",
            expectedVersion: 0);
    }
}
