// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Spam.Scanning;
using MailFathom.Infrastructure.Spam;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Spam;

/// <summary>Covers what a daemon's answer becomes once it crosses the scanning port.</summary>
/// <remarks>
/// The socket is not reachable from here — <c>backend/tests/AGENTS.md</c> keeps the network out of this suite — so what these
/// cover is the mapping the exchange hands its answer to. That the exchange itself happens, and that a real daemon
/// answers in this shape at all, is the integration suite's claim.
/// </remarks>
public sealed class SpamAssassinScannerTests
{
    private const string CorpusRevision = "spamassassin.4.0.2+2025-08-27";

    /// <summary>A scored answer becomes a score, a threshold, the rules, and the corpus that fired them.</summary>
    [Fact]
    public void Scored_AnAnswerCarryingAVerdict_BecomesThatVerdictWithTheRulesAndTheCorpus()
    {
        // Arrange
        var reply = ReplyTo("Spam: True ; 1000.0 / 5.0", "GTUBE,NO_RECEIVED,NO_RELAYS");

        // Act
        var result = SpamAssassinScanner.Scored(reply, CorpusRevision);

        // Assert
        Assert.Equal(SpamScanOutcome.Scored, result.Outcome);
        Assert.Equal(1000.0, result.Assessment!.Score);
        Assert.Equal(5.0, result.Assessment.Threshold);
        Assert.True(result.Assessment.ClearsThreshold);
        Assert.Equal(["GTUBE", "NO_RECEIVED", "NO_RELAYS"], result.FiredRules);
        Assert.Equal(CorpusRevision, result.CorpusRevision);
    }

    /// <summary>Ordinary mail scores below the threshold and is reported as scored rather than as spam.</summary>
    [Fact]
    public void Scored_AnAnswerBelowTheThreshold_IsScoredWithoutBeingSpam()
    {
        // Arrange
        var reply = ReplyTo("Spam: False ; -0.0 / 5.0", "NO_RELAYS");

        // Act
        var result = SpamAssassinScanner.Scored(reply, CorpusRevision);

        // Assert
        Assert.Equal(SpamScanOutcome.Scored, result.Outcome);
        Assert.False(result.Assessment!.ClearsThreshold);
        Assert.Equal(["NO_RELAYS"], result.FiredRules);
    }

    /// <summary>An answer stating no numbers is an unavailable scanner rather than a message that scored zero.</summary>
    /// <remarks>
    /// This is the distinction the outcome exists for. A zero would be recorded as a message a corpus read and found
    /// clean, which is a stronger claim than "nothing scored it" and the one a reader would act on.
    /// </remarks>
    [Fact]
    public void Scored_AnAnswerStatingNoNumbers_IsReportedAsUnavailableRatherThanAsAZeroScore()
    {
        // Arrange
        var reply = ReplyTo("Content-length: 0", string.Empty);

        // Act
        var result = SpamAssassinScanner.Scored(reply, CorpusRevision);

        // Assert
        Assert.Equal(SpamScanOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Assessment);
        Assert.Empty(result.FiredRules);
        Assert.Null(result.CorpusRevision);
    }

    /// <summary>A corpus that fired nothing leaves no rules behind, whatever the line's own punctuation was.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData(",")]
    [InlineData(" , ")]
    public void FiredRules_ABodyNamingNoRule_YieldsNone(string body)
    {
        // Act
        var rules = SpamAssassinScanner.FiredRules(body);

        // Assert
        Assert.Empty(rules);
    }

    /// <summary>The names are read out of one comma-separated line, whatever spacing and line ending it carries.</summary>
    /// <remarks>
    /// Whether the line ends with a break varies between protocol versions by the protocol's own admission, which is why
    /// the parts are trimmed rather than the line being required to end a particular way.
    /// </remarks>
    [Theory]
    [InlineData("GTUBE,NO_RECEIVED")]
    [InlineData("GTUBE, NO_RECEIVED")]
    [InlineData("GTUBE,NO_RECEIVED\r\n")]
    [InlineData("  GTUBE ,\tNO_RECEIVED  ")]
    public void FiredRules_ALineWrittenAnyOfTheWaysTheDaemonWritesIt_YieldsTheSameNames(string body)
    {
        // Act
        var rules = SpamAssassinScanner.FiredRules(body);

        // Assert
        Assert.Equal(["GTUBE", "NO_RECEIVED"], rules);
    }

    private static SpamdReply ReplyTo(string verdictHeader, string body)
    {
        _ = SpamdReply.TryParse(
            Encoding.ASCII.GetBytes($"SPAMD/1.1 0 EX_OK\r\n{verdictHeader}\r\n\r\n{body}"),
            out var reply);

        return reply!;
    }
}
