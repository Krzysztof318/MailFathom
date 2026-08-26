// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Infrastructure.Persistence.Settings;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Settings;

/// <summary>
/// Covers what a candidate has to satisfy before a statement is issued for it. The bound is the one the read enforces
/// from the other direction, so what matters here is that the two measure the same thing: a document accepted on its
/// compact length and stored larger than that is a row the next start refuses on a change that was reported as
/// committed.
/// </summary>
public sealed class RootSettingsCommitRulesTests
{
    /// <summary>The stored size counts the separators the database's own rendering inserts, which the compact form has none of.</summary>
    [Fact]
    public void PersistedOctetsOf_ADocumentOfProperties_CountsWhatTheDatabaseStores()
    {
        // Arrange
        const string compact = """{"a":"1","b":"2"}""";

        // Act
        var persisted = RootSettingsCommitRules.PersistedOctetsOf(compact);

        // Assert
        Assert.True(
            persisted >= """{"a": "1", "b": "2"}""".Length,
            $"The stored form is longer than the compact one, and {persisted} does not reach it.");
    }

    /// <summary>An array's elements are separated too, so the measure counts them rather than only an object's properties.</summary>
    [Fact]
    public void PersistedOctetsOf_ADocumentCarryingAnArray_CountsItsElementSeparators()
    {
        // Arrange
        const string compact = """{"a":["1","2","3"]}""";

        // Act
        var persisted = RootSettingsCommitRules.PersistedOctetsOf(compact);

        // Assert
        Assert.True(
            persisted >= """{"a": ["1", "2", "3"]}""".Length,
            $"The stored form is longer than the compact one, and {persisted} does not reach it.");
    }

    /// <summary>A document whose compact form fits but whose stored form does not is refused rather than persisted.</summary>
    [Fact]
    public void FitsWhatIsComposedFrom_ADocumentThatOnlyFitsCompacted_IsRefused()
    {
        // Arrange
        var properties = Enumerable.Range(0, 74_000).Select(position => $"\"k{position}\":\"v\"");
        var compact = $"{{{string.Join(',', properties)}}}";

        // Act & Assert
        Assert.True(compact.Length <= RootSettingsDocument.MaximumOctets, "The document has to fit compacted for the claim to mean anything.");
        Assert.False(RootSettingsCommitRules.FitsWhatIsComposedFrom(compact));
    }

    /// <summary>The document the deployment starts with fits, so the bound refuses nothing an ordinary write produces.</summary>
    [Fact]
    public void FitsWhatIsComposedFrom_AnOrdinaryDocument_Fits()
    {
        // Act & Assert
        Assert.True(RootSettingsCommitRules.FitsWhatIsComposedFrom("""{ "MailboxSearch": { "SnippetsPerEmail": "3" } }"""));
    }

    /// <summary>A candidate past what the layer composes settings from is a caller's mistake rather than a statement to attempt.</summary>
    [Fact]
    public void RefuseWhatCannotBeCommitted_ADocumentPastWhatTheLayerComposesFrom_IsRefused()
    {
        // Arrange
        var oversized = $$"""{ "Padding": "{{new string('x', RootSettingsDocument.MaximumOctets)}}" }""";

        // Act & Assert
        var refusal = Assert.Throws<ArgumentException>(
            () => RootSettingsCommitRules.RefuseWhatCannotBeCommitted(oversized, expectedVersion: 1));
        Assert.Contains("octets", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A candidate that is not a document at all is refused before anything parses it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefuseWhatCannotBeCommitted_ACandidateThatIsNotADocument_IsRefused(string json)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => RootSettingsCommitRules.RefuseWhatCannotBeCommitted(json, expectedVersion: 1));
    }

    /// <summary>
    /// Every one of these is a valid <c>jsonb</c> value, so the column's own cast would store each of them and the next
    /// start would then refuse to read the row: a configuration layer is composed from colon-delimited keys, and only
    /// an object has any. The refusal belongs on this side for that reason.
    /// </summary>
    /// <param name="json">A JSON value whose root is not an object.</param>
    [Theory]
    [InlineData("[]")]
    [InlineData("""["Persistence:Password", "plaintext"]""")]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("\"not settings\"")]
    public void RefuseWhatCannotBeCommitted_ACandidateWhoseRootIsNotAnObject_IsRefused(string json)
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(
            () => RootSettingsCommitRules.RefuseWhatCannotBeCommitted(json, expectedVersion: 1));

        // Assert
        Assert.Contains("root is not an object", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Text that is not JSON at all is the one refusal the column would also take, and it arrives here as the reader's
    /// own exception. It leaves as an argument refusal like every other candidate this type turns away, so a caller has
    /// one exception type to answer rather than two.
    /// </summary>
    [Fact]
    public void RefuseWhatCannotBeCommitted_ACandidateThatIsNotJson_IsRefusedAsAnArgument()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(
            () => RootSettingsCommitRules.RefuseWhatCannotBeCommitted("{ not json", expectedVersion: 1));

        // Assert
        Assert.Contains("is not JSON", refusal.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<JsonException>(refusal.InnerException);
    }

    /// <summary>No document ever stood at a negative version, so a commit against one is refused.</summary>
    [Fact]
    public void RefuseWhatCannotBeCommitted_ANegativeExpectedVersion_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RootSettingsCommitRules.RefuseWhatCannotBeCommitted("{}", expectedVersion: -1));
    }
}
