// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Domain.UnitTests.Spam;

public sealed class SpamClassificationTests
{
    private static readonly StoredEmailId Occurrence = StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001"));

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);

    private static readonly SpamClassificationProfile Profile =
        SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5);

    [Fact]
    public void Create_MoreSignalsThanTheBoundAdmits_KeepsTheOnesTheStagesProducedFirst()
    {
        // Arrange
        var written = Enumerable
            .Range(0, SpamClassification.MaximumSignals + 20)
            .Select(index => Signal($"rule-{index}"))
            .ToArray();

        // Act
        var classification = Classification(signals: written);

        // Assert
        Assert.Equal(
            written.Take(SpamClassification.MaximumSignals).Select(signal => signal.Name),
            classification.Signals.Select(signal => signal.Name));
    }

    [Fact]
    public void Create_AnEvaluationTimeAtAnotherOffset_IsHeldAsTheInstantItNames()
    {
        // Arrange
        var written = new DateTimeOffset(2026, 8, 12, 11, 30, 0, TimeSpan.FromHours(2));

        // Act
        var classification = Classification(evaluatedAt: written);

        // Assert
        Assert.Equal(TimeSpan.Zero, classification.EvaluatedAt.Offset);
        Assert.Equal(written.UtcDateTime, classification.EvaluatedAt.UtcDateTime);
    }

    [Fact]
    public void Create_ACorpusRevisionLongerThanTheBound_IsRefused()
    {
        // Arrange
        var written = new string('a', SpamClassification.MaximumCorpusRevisionLength + 1);

        // Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(corpusRevision: written));

        Assert.Equal("corpusRevision", failure.ParamName);
    }

    [Fact]
    public void Create_ACorpusRevisionCarryingAControlCharacter_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(corpusRevision: "3.4\u00071"));

        Assert.Equal("corpusRevision", failure.ParamName);
    }

    /// <summary>The deterministic stage has no corpus, so absence is a supported record rather than a missing value.</summary>
    [Fact]
    public void Create_NoCorpusRevision_RecordsNone()
    {
        // Arrange, Act
        var classification = Classification();

        // Assert
        Assert.Null(classification.CorpusRevision);
    }

    [Fact]
    public void Create_AVerdictOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Classification(verdict: (SpamVerdict)42));
    }

    [Fact]
    public void Create_AStageOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Classification(decidedBy: (SpamClassificationStage)42));
    }

    [Fact]
    public void Create_NoSignalList_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => SpamClassification.Create(
            Occurrence,
            SpamVerdict.Undetermined,
            SpamClassificationStage.Deterministic,
            assessment: null,
            corpusRevision: null,
            Profile,
            signals: null!,
            EvaluatedAt));
    }

    /// <summary>A record written before the profile joined it carries none, and is read back rather than refused.</summary>
    [Fact]
    public void Create_NoProfile_RecordsTermsNothingCanCompare()
    {
        // Arrange, Act
        var classification = Classification(profile: default(SpamClassificationProfile));

        // Assert
        Assert.False(classification.Profile.IsSpecified);
    }

    [Fact]
    public void Create_AProfile_RecordsTheTermsTheVerdictWasReachedUnder()
    {
        // Arrange, Act
        var classification = Classification();

        // Assert
        Assert.Equal(Profile, classification.Profile);
    }

    private static SpamClassification Classification(
        SpamVerdict verdict = SpamVerdict.Spam,
        SpamClassificationStage decidedBy = SpamClassificationStage.Deterministic,
        string? corpusRevision = null,
        SpamClassificationProfile? profile = null,
        IReadOnlyList<SpamSignal>? signals = null,
        DateTimeOffset? evaluatedAt = null) => SpamClassification.Create(
        Occurrence,
        verdict,
        decidedBy,
        assessment: null,
        corpusRevision,
        profile ?? Profile,
        signals ?? [],
        evaluatedAt ?? EvaluatedAt);

    private static SpamSignal Signal(string name) => SpamSignal.Create(
        SpamSignalKind.ScannerRule,
        name,
        observation: null,
        SpamSignalProvenance.FromScannerCorpus("4.0.2"));
}
