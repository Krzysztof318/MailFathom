// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Spam;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Spam;

/// <summary>Covers what a scan records as the corpus it ran under, which the protocol carries nowhere directly.</summary>
public sealed class SpamAssassinCorpusTests
{
    /// <summary>The release and its build date, read out of the header a rewriting command adds.</summary>
    /// <remarks>
    /// The header is exactly what the pinned image writes. The host name it ends with is the container the daemon
    /// happens to run in, so it is deliberately absent from the answer: two deployments scanning identically would
    /// otherwise record different corpora, and it is a host name besides.
    /// </remarks>
    [Fact]
    public void Identify_TheHeaderTheDaemonWrites_NamesTheReleaseAndItsBuildWithoutTheHostName()
    {
        // Arrange
        var reply = ReplyTo(
            "X-Spam-Checker-Version: SpamAssassin 4.0.2 (2025-08-27) on 6f2a30ce15e2\r\nX-Spam-Level: \r\n");

        // Act
        var corpus = SpamAssassinCorpus.Identify(reply);

        // Assert
        Assert.Equal("spamassassin.4.0.2+2025-08-27", corpus);
        Assert.DoesNotContain("6f2a30ce15e2", corpus, StringComparison.Ordinal);
    }

    /// <summary>A release that states no build date is named by its release alone.</summary>
    [Fact]
    public void Identify_AReleaseWithoutABuildDate_NamesTheReleaseAlone()
    {
        // Arrange
        var reply = ReplyTo("X-Spam-Checker-Version: SpamAssassin 4.1.0 on host\r\n");

        // Act
        var corpus = SpamAssassinCorpus.Identify(reply);

        // Assert
        Assert.Equal("spamassassin.4.1.0", corpus);
    }

    /// <summary>A daemon whose own configuration removed that header still names something, and it says what.</summary>
    /// <remarks>
    /// The fallback is the protocol version, which is what the answer does carry. It is a weaker identity by design and
    /// a differently shaped one on purpose: a reader comparing two classifications can see at a glance that one of them
    /// was reached against a daemon that would not name its release.
    /// </remarks>
    [Fact]
    public void Identify_ADaemonThatNamesNoRelease_FallsBackToTheProtocolItSpoke()
    {
        // Arrange
        var reply = ReplyTo("Subject: nothing the mapping reads\r\n");

        // Act
        var corpus = SpamAssassinCorpus.Identify(reply);

        // Assert
        Assert.Equal("spamassassin+spamd.1.1", corpus);
    }

    /// <summary>What a daemon writes cannot widen the value a signal is stored under.</summary>
    /// <remarks>
    /// The origin a signal carries is bounded and refuses rather than truncates, so an over-long release would
    /// otherwise be the reason a whole classification could not be recorded.
    /// </remarks>
    [Fact]
    public void Identify_AReleaseLongerThanAnyone_StaysInsideWhatASignalOriginAccepts()
    {
        // Arrange
        var reply = ReplyTo(
            $"X-Spam-Checker-Version: SpamAssassin {new string('9', 400)} ({new string('8', 400)}) on host\r\n");

        // Act
        var corpus = SpamAssassinCorpus.Identify(reply);

        // Assert
        Assert.True(
            corpus.Length <= SpamSignalProvenance.MaximumOriginLength,
            $"A corpus of {corpus.Length} characters cannot be recorded as a signal origin.");

        // Recorded rather than merely measured, because the bound this is written against is that type's.
        Assert.Equal(corpus, SpamSignalProvenance.FromScannerCorpus(corpus).Origin);
    }

    private static SpamdReply ReplyTo(string rewrittenHeaders)
    {
        _ = SpamdReply.TryParse(
            Encoding.ASCII.GetBytes($"SPAMD/1.1 0 EX_OK\r\nSpam: False ; -0.0 / 5.0\r\n\r\n{rewrittenHeaders}"),
            out var reply);

        return reply!;
    }
}
