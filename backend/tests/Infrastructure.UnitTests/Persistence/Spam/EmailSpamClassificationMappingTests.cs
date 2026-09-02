// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Spam;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Spam;

public sealed class EmailSpamClassificationMappingTests
{
    private static readonly StoredEmailId Occurrence =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000abcd"));

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>One classification written and read back is the same record, which is what one mapping for both buys.</summary>
    [Fact]
    public void Read_ARecordWrittenByTheSameMapping_ComesBackUnchanged()
    {
        // Arrange
        var classification = ClassificationWith(
            SpamSignal.Create(
                SpamSignalKind.ProviderSpamVerdict,
                "X-Spam-Flag",
                "YES",
                SpamSignalProvenance.FromMessageHeader("X-Spam-Flag")),
            SpamSignal.Create(
                SpamSignalKind.ScannerRule,
                "BAYES_99",
                observation: null,
                SpamSignalProvenance.FromScannerCorpus("4.0.2")));

        var entity = EmptyRow();

        // Act
        EmailSpamClassificationMapping.Write(entity, classification);

        foreach (var row in EmailSpamClassificationMapping.SignalRows(classification))
        {
            entity.Signals.Add(row);
        }

        // Assert
        var read = EmailSpamClassificationMapping.Read(entity);

        Assert.Equal(classification.EmailId, read.EmailId);
        Assert.Equal(classification.Verdict, read.Verdict);
        Assert.Equal(classification.DecidedBy, read.DecidedBy);
        Assert.Equal(classification.Assessment, read.Assessment);
        Assert.Equal(classification.CorpusRevision, read.CorpusRevision);
        Assert.Equal(classification.EvaluatedAt, read.EvaluatedAt);
        Assert.Equal(classification.Profile, read.Profile);
        Assert.Equal(classification.Signals, read.Signals);
    }

    /// <summary>The terms a verdict was reached under are what a run reads to decide whether to score a message again.</summary>
    [Fact]
    public void Write_ARecordNamingTheTermsItWasReachedUnder_WritesThemAsTheIdentityTheyAre()
    {
        // Arrange
        var entity = EmptyRow();
        var profile = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5);

        // Act
        EmailSpamClassificationMapping.Write(entity, ClassificationWith(profile));

        // Assert
        Assert.Equal(profile.Value, entity.Profile);
    }

    /// <summary>A row written before the profile joined the record names terms nothing can compare, rather than none.</summary>
    [Fact]
    public void Read_ARowNamingNoProfile_ReadsTermsNothingCanCompare()
    {
        // Arrange
        var entity = EmptyRow();

        // Act
        var classification = EmailSpamClassificationMapping.Read(entity);

        // Assert
        Assert.False(classification.Profile.IsSpecified);
    }

    /// <summary>The order the stages produced the facts in is what a truncated record's meaning rests on.</summary>
    [Fact]
    public void SignalRows_SeveralSignals_NumbersThemInTheOrderTheStagesProducedThem()
    {
        // Arrange
        var classification = ClassificationWith(
            SpamSignal.Create(
                SpamSignalKind.JunkFolderPlacement,
                "JUNK",
                observation: null,
                SpamSignalProvenance.FromFolderPlacement("JUNK")),
            SpamSignal.Create(
                SpamSignalKind.SenderAuthentication,
                "dmarc",
                "fail",
                SpamSignalProvenance.FromMessageHeader("Authentication-Results")));

        // Act
        var rows = EmailSpamClassificationMapping.SignalRows(classification).ToArray();

        // Assert
        Assert.Equal([0, 1], rows.Select(row => row.Ordinal));
        Assert.Equal(["JUNK", "dmarc"], rows.Select(row => row.Name));
        Assert.All(rows, row => Assert.Equal(Occurrence.Value, row.StoredEmailId));
    }

    /// <summary>Signals read back out of order are still one record, because the ordinal rather than the row order carries it.</summary>
    [Fact]
    public void Read_SignalRowsInAnyOrder_RestoresThemByOrdinal()
    {
        // Arrange
        var entity = EmptyRow();

        entity.Signals.Add(
            SignalRow(1, SpamSignalKind.ScannerRule, "BAYES_99", SpamSignalSource.ScannerCorpus, "4.0.2"));
        entity.Signals.Add(
            SignalRow(0, SpamSignalKind.ProviderSpamVerdict, "X-Spam-Flag", SpamSignalSource.MessageHeader, "X-Spam-Flag"));

        // Act
        var classification = EmailSpamClassificationMapping.Read(entity);

        // Assert
        Assert.Equal(["X-Spam-Flag", "BAYES_99"], classification.Signals.Select(signal => signal.Name));
    }

    /// <summary>An assessment is a pair, so a row holding only half of one carries none rather than a number in no scale.</summary>
    [Theory]
    [InlineData(15.2, null)]
    [InlineData(null, 5.0)]
    [InlineData(null, null)]
    public void Read_ARowHoldingOnlyHalfAnAssessment_ReadsNoAssessment(double? score, double? threshold)
    {
        // Arrange
        var entity = EmptyRow();
        entity.Score = score;
        entity.Threshold = threshold;

        // Act
        var classification = EmailSpamClassificationMapping.Read(entity);

        // Assert
        Assert.Null(classification.Assessment);
    }

    [Fact]
    public void Write_ARecordCarryingAnAssessment_WritesBothNumbers()
    {
        // Arrange
        var entity = EmptyRow();
        var classification = SpamClassification.Create(
            Occurrence,
            SpamVerdict.Spam,
            SpamClassificationStage.Scanner,
            SpamAssessment.Create(9.5, 5.0),
            "4.0.2",
            SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5.0),
            [],
            EvaluatedAt);

        // Act
        EmailSpamClassificationMapping.Write(entity, classification);

        // Assert
        Assert.Equal(9.5, entity.Score);
        Assert.Equal(5.0, entity.Threshold);
        Assert.Equal("4.0.2", entity.CorpusRevision);
        Assert.Equal(SpamClassificationStage.Scanner, entity.DecidedBy);
        Assert.Equal(SpamVerdict.Spam, entity.Verdict);
        Assert.Equal(EvaluatedAt, entity.EvaluatedAt);
    }

    private static SpamClassification ClassificationWith(params SpamSignal[] signals) =>
        ClassificationWith(SpamClassificationProfile.Create(usesScanner: false, scannerThreshold: null), signals);

    private static SpamClassification ClassificationWith(
        SpamClassificationProfile profile,
        params SpamSignal[] signals) => SpamClassification.Create(
        Occurrence,
        SpamVerdict.Spam,
        SpamClassificationStage.Deterministic,
        assessment: null,
        corpusRevision: null,
        profile,
        signals,
        EvaluatedAt);

    private static EmailSpamClassificationEntity EmptyRow() => new()
    {
        StoredEmailId = Occurrence.Value,
        Verdict = SpamVerdict.Undetermined,
        DecidedBy = SpamClassificationStage.Deterministic,
        EvaluatedAt = EvaluatedAt,
    };

    private static EmailSpamClassificationSignalEntity SignalRow(
        int ordinal,
        SpamSignalKind kind,
        string name,
        SpamSignalSource source,
        string origin) => new()
        {
            StoredEmailId = Occurrence.Value,
            Ordinal = ordinal,
            Kind = kind,
            Name = name,
            Source = source,
            Origin = origin,
        };
}
