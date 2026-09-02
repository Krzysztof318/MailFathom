// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Infrastructure.Spam;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Spam;

/// <summary>Covers the reading of what a spam daemon writes back, which is untrusted input from a separate process.</summary>
/// <remarks>
/// The bytes here are the daemon's own, recorded from the pinned image rather than imagined: the status line, the
/// header block, and the comma-separated symbol line are exactly what a scan answers with. What the integration suite
/// settles is that a real daemon still writes them; what this settles is that nothing malformed reaches a caller as a
/// score.
/// </remarks>
public sealed class SpamdReplyTests
{
    /// <summary>The whole of what a scoring command answers with, read into its three parts.</summary>
    [Fact]
    public void TryParse_AScoringAnswer_ReadsTheVersionTheHeadersAndTheSymbolLine()
    {
        // Arrange
        var answer = Answer(
            "SPAMD/1.1 0 EX_OK\r\nContent-length: 43\r\nSpam: True ; 1000.0 / 5.0\r\n\r\nGTUBE,NO_RECEIVED,NO_RELAYS");

        // Act
        var parsed = SpamdReply.TryParse(answer, out var reply);

        // Assert
        Assert.True(parsed);
        Assert.NotNull(reply);
        Assert.Equal("1.1", reply.ProtocolVersion);
        Assert.Equal("True ; 1000.0 / 5.0", reply.Headers["Spam"]);
        Assert.Equal("GTUBE,NO_RECEIVED,NO_RELAYS", reply.Body);
    }

    /// <summary>The header name is matched the way the protocol treats it, so a daemon's own casing cannot hide the verdict.</summary>
    [Fact]
    public void TryParse_AHeaderInAnotherCasing_IsStillFound()
    {
        // Arrange
        var answer = Answer("SPAMD/1.1 0 EX_OK\r\nspam: False ; 2.0 / 5.0\r\n\r\n");

        // Act
        _ = SpamdReply.TryParse(answer, out var reply);

        // Assert
        Assert.NotNull(reply);
        Assert.True(reply.TryReadAssessment(out var score, out var threshold));
        Assert.Equal(2.0, score);
        Assert.Equal(5.0, threshold);
    }

    /// <summary>A negative zero is a score the daemon really writes for ordinary mail, and it is a number.</summary>
    [Fact]
    public void TryReadAssessment_TheScoreOrdinaryMailReceives_IsReadAsANumber()
    {
        // Arrange
        _ = SpamdReply.TryParse(Answer("SPAMD/1.1 0 EX_OK\r\nSpam: False ; -0.0 / 5.0\r\n\r\nNO_RELAYS"), out var reply);

        // Act
        var read = reply!.TryReadAssessment(out var score, out var threshold);

        // Assert
        Assert.True(read);
        Assert.Equal(0.0, score);
        Assert.Equal(5.0, threshold);
    }

    /// <summary>Anything that is not this protocol's successful answer is no answer at all.</summary>
    /// <remarks>
    /// A refusal is included deliberately. The daemon closes the connection after a non-zero status line, so there is
    /// nothing else to read and no caller here acts differently on which refusal it was — reporting it as a reply with a
    /// code on it would be a distinction nothing uses.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("SPAMD/1.1 76 EX_PROTOCOL\r\n\r\n")]
    [InlineData("SPAMD/ 0 EX_OK\r\n\r\n")]
    [InlineData("SPAMD/1.1\r\n\r\n")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("<html><body>Gateway</body></html>")]
    public void TryParse_AnythingButASuccessfulAnswer_IsRefused(string written)
    {
        // Arrange
        var answer = Answer(written);

        // Act
        var parsed = SpamdReply.TryParse(answer, out var reply);

        // Assert
        Assert.False(parsed);
        Assert.Null(reply);
    }

    /// <summary>A verdict header the adapter cannot read a pair of numbers out of yields no assessment.</summary>
    /// <remarks>
    /// Each of these parses as a reply — the daemon answered and the status line was well formed — and none of them
    /// states a score, which is the distinction that keeps a message from being recorded as scored and clean when
    /// nothing scored it.
    /// </remarks>
    [Theory]
    [InlineData("SPAMD/1.1 0 EX_OK\r\nContent-length: 0\r\n\r\n")]
    [InlineData("SPAMD/1.1 0 EX_OK\r\nSpam: True\r\n\r\n")]
    [InlineData("SPAMD/1.1 0 EX_OK\r\nSpam: True ; 15.0\r\n\r\n")]
    [InlineData("SPAMD/1.1 0 EX_OK\r\nSpam: True ; high / 5.0\r\n\r\n")]
    [InlineData("SPAMD/1.1 0 EX_OK\r\nSpam: True ; NaN / 5.0\r\n\r\n")]
    [InlineData("SPAMD/1.1 0 EX_OK\r\nSpam: True ; Infinity / 5.0\r\n\r\n")]
    public void TryReadAssessment_AVerdictWithoutAUsablePairOfNumbers_IsNotRead(string written)
    {
        // Arrange
        _ = SpamdReply.TryParse(Answer(written), out var reply);

        // Act
        var read = reply!.TryReadAssessment(out _, out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>A header the adapter does not know is skipped rather than refused, as the protocol requires of a client.</summary>
    [Fact]
    public void TryParse_HeadersThisAdapterDoesNotKnow_AreKeptWithoutRefusingTheAnswer()
    {
        // Arrange
        var answer = Answer(
            "SPAMD/1.1 0 EX_OK\r\nSomething-Later: yes\r\nSpam: True ; 9.0 / 5.0\r\nAnd-Another: 1\r\n\r\nRULE");

        // Act
        var parsed = SpamdReply.TryParse(answer, out var reply);

        // Assert
        Assert.True(parsed);
        Assert.True(reply!.TryReadAssessment(out var score, out _));
        Assert.Equal(9.0, score);
    }

    private static ReadOnlySpan<byte> Answer(string written) => Encoding.ASCII.GetBytes(written);
}
