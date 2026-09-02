// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Signals;

public sealed class DeterministicSpamClassifierTests
{
    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    [Fact]
    public void Read_AMessageCarryingNothing_IsUndeterminedWithNoSignals()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(SpamHeaderFacts.None, Inbox, isJunkFolder: false);

        // Assert
        Assert.Equal(SpamVerdict.Undetermined, reading.Verdict);
        Assert.Empty(reading.Signals);
        Assert.Null(reading.Assessment);
    }

    [Fact]
    public void Read_AnOccurrenceInTheJunkFolder_IsSpamAndRecordsThePlacement()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(SpamHeaderFacts.None, Junk, isJunkFolder: true);

        // Assert
        var signal = Assert.Single(reading.Signals);

        Assert.Equal(SpamVerdict.Spam, reading.Verdict);
        Assert.Equal(SpamSignalKind.JunkFolderPlacement, signal.Kind);
        Assert.Equal("JUNK", signal.Name);
        Assert.Null(signal.Observation);
        Assert.Equal(SpamSignalSource.FolderPlacement, signal.Provenance.Source);
    }

    /// <summary>The placement is a decision somebody already acted on, so it outranks a header saying otherwise.</summary>
    [Fact]
    public void Read_AnOccurrenceInTheJunkFolderWhoseProviderSaidNo_IsStillSpam()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = FactsWithHeaders(("X-Spam-Flag", "NO"));

        // Act
        var reading = classifier.Read(facts, Junk, isJunkFolder: true);

        // Assert
        Assert.Equal(SpamVerdict.Spam, reading.Verdict);
    }

    [Theory]
    [InlineData("YES", SpamVerdict.Spam)]
    [InlineData("yes", SpamVerdict.Spam)]
    [InlineData("NO", SpamVerdict.NotSpam)]
    [InlineData("no", SpamVerdict.NotSpam)]
    public void Read_AProviderSpamFlag_ReachesTheVerdictItStates(string flag, SpamVerdict expected)
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(FactsWithHeaders(("X-Spam-Flag", flag)), Inbox, isJunkFolder: false);

        // Assert
        Assert.Equal(expected, reading.Verdict);
    }

    [Theory]
    [InlineData("Yes, score=15.2 required=5.0 tests=BAYES_99", SpamVerdict.Spam)]
    [InlineData("No, score=-2.6 required=5.0 tests=BAYES_00", SpamVerdict.NotSpam)]
    [InlineData("Maybe, score=1.0 required=5.0", SpamVerdict.Undetermined)]
    public void Read_AProviderSpamStatus_ReachesTheVerdictItsFirstWordStates(string status, SpamVerdict expected)
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(FactsWithHeaders(("X-Spam-Status", status)), Inbox, isJunkFolder: false);

        // Assert
        Assert.Equal(expected, reading.Verdict);
    }

    [Fact]
    public void Read_AStatusCarryingBothNumbers_RecordsThemAsOneAssessment()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = FactsWithHeaders(("X-Spam-Status", "Yes, score=15.2 required=5.0 tests=BAYES_99"));

        // Act
        var reading = classifier.Read(facts, Inbox, isJunkFolder: false);

        // Assert
        Assert.NotNull(reading.Assessment);

        var assessment = reading.Assessment;

        Assert.Equal(15.2, assessment.Score);
        Assert.Equal(5.0, assessment.Threshold);
        Assert.True(assessment.ClearsThreshold);
    }

    /// <summary>A score in an unknown scale is a signal rather than a measurement, so no assessment is recorded from one.</summary>
    [Theory]
    [InlineData("X-Spam-Status", "Yes, score=15.2")]
    [InlineData("X-Spam-Score", "15.2")]
    [InlineData("X-Spam-Level", "***************")]
    public void Read_AScoreWithNoThresholdBesideIt_RecordsNoAssessment(string field, string value)
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(FactsWithHeaders((field, value)), Inbox, isJunkFolder: false);

        // Assert
        Assert.Null(reading.Assessment);
        Assert.Contains(reading.Signals, signal => signal.Kind is SpamSignalKind.ProviderSpamVerdict);
    }

    /// <summary>A single word with two accepted values answers a message that disagrees with itself.</summary>
    [Fact]
    public void Read_AFlagAndAStatusThatDisagree_IsAnsweredByTheFlag()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = FactsWithHeaders(
            ("X-Spam-Flag", "NO"),
            ("X-Spam-Status", "Yes, score=15.2 required=5.0"));

        // Act
        var reading = classifier.Read(facts, Inbox, isJunkFolder: false);

        // Assert
        Assert.Equal(SpamVerdict.NotSpam, reading.Verdict);
    }

    /// <summary>A failure the receiving server chose to deliver anyway is recorded, and does not become a spam verdict.</summary>
    [Fact]
    public void Read_AnAuthenticationFailure_IsRecordedWithoutDecidingTheVerdict()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = SpamHeaderFacts.Create(
            [new MessageAuthenticationResult("dmarc", "fail", "header.from=example.test", IsForwarded: false)],
            []);

        // Act
        var reading = classifier.Read(facts, Inbox, isJunkFolder: false);

        // Assert
        var signal = Assert.Single(reading.Signals);

        Assert.Equal(SpamVerdict.Undetermined, reading.Verdict);
        Assert.Equal(SpamSignalKind.SenderAuthentication, signal.Kind);
        Assert.Equal("dmarc", signal.Name);
        Assert.Equal("fail header.from=example.test", signal.Observation);
        Assert.Equal("Authentication-Results", signal.Provenance.Origin);
    }

    /// <summary>An ARC outcome is a claim a relay signed, so it is kept apart from what this mailbox's server saw.</summary>
    [Fact]
    public void Read_AForwardedAuthenticationResult_IsRecordedAsItsOwnKindAndOrigin()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = SpamHeaderFacts.Create(
            [new MessageAuthenticationResult("spf", "pass", Detail: null, IsForwarded: true)],
            []);

        // Act
        var reading = classifier.Read(facts, Inbox, isJunkFolder: false);

        // Assert
        var signal = Assert.Single(reading.Signals);

        Assert.Equal(SpamSignalKind.ForwardedSenderAuthentication, signal.Kind);
        Assert.Equal("pass", signal.Observation);
        Assert.Equal("ARC-Authentication-Results", signal.Provenance.Origin);
    }

    /// <summary>Truncating a record keeps what the verdict rests on, so the placement leads whatever else was observed.</summary>
    [Fact]
    public void Read_APlacementBesideHeaders_PutsThePlacementFirst()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = SpamHeaderFacts.Create(
            [new MessageAuthenticationResult("spf", "pass", Detail: null, IsForwarded: false)],
            [new ProviderSpamHeaderValue("X-Spam-Flag", "YES")]);

        // Act
        var reading = classifier.Read(facts, Junk, isJunkFolder: true);

        // Assert
        Assert.Equal(
            [SpamSignalKind.JunkFolderPlacement, SpamSignalKind.SenderAuthentication, SpamSignalKind.ProviderSpamVerdict],
            reading.Signals.Select(signal => signal.Kind));
    }

    [Fact]
    public void Read_NoFacts_IsRefused()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => classifier.Read(facts: null!, Inbox, isJunkFolder: false));
    }

    private static SpamHeaderFacts FactsWithHeaders(params (string Field, string Value)[] headers) =>
        SpamHeaderFacts.Create(
            [],
            [.. headers.Select(header => new ProviderSpamHeaderValue(header.Field, header.Value))]);
}
