// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery;

/// <summary>
/// Covers the subject an answer carries: that one prefix is written, that a prefix already there is recognized however
/// it was spelled and in whatever language, and that nothing a header could not carry survives into it.
/// </summary>
public sealed class ResponseSubjectTests
{
    /// <summary>A subject carrying no prefix takes this system's own.</summary>
    [Fact]
    public void ForReply_SubjectWithoutAPrefix_TakesTheReplyPrefix()
    {
        // Act
        var subject = ResponseSubject.ForReply("Quarterly report");

        // Assert
        Assert.Equal("Re: Quarterly report", subject);
    }

    /// <summary>
    /// A prefix already there is left alone whichever client wrote it, because adding a second produces the
    /// <c>Re: Re: Re:</c> a thread becomes unreadable as. The set is the prefixes in common use rather than the English
    /// one, so a correspondent writing in German or Polish is recognized as well.
    /// </summary>
    [Theory]
    [InlineData("Re: Quarterly report")]
    [InlineData("RE: Quarterly report")]
    [InlineData("re:Quarterly report")]
    [InlineData("Re[2]: Quarterly report")]
    [InlineData("Aw: Quarterly report")]
    [InlineData("Antwort: Quarterly report")]
    [InlineData("Odp: Quarterly report")]
    [InlineData("Sv: Quarterly report")]
    [InlineData("Ynt: Quarterly report")]
    [InlineData("回复: Quarterly report")]
    public void ForReply_SubjectAlreadyCarryingAPrefix_IsLeftAsItWasWritten(string answeredSubject)
    {
        // Act
        var subject = ResponseSubject.ForReply(answeredSubject);

        // Assert
        Assert.Equal(answeredSubject, subject);
    }

    /// <summary>
    /// A language's two markers mean opposite things, so a forward's is not a reply's. Finnish writes <c>Vl</c> for a
    /// forward and <c>Vs</c> for a reply, and replying to a forwarded message takes the reply prefix like any other.
    /// </summary>
    [Fact]
    public void ForReply_SubjectCarryingAForwardPrefix_TakesTheReplyPrefixAsWell()
    {
        // Act
        var subject = ResponseSubject.ForReply("Vl: Kokousmuistio");

        // Assert
        Assert.Equal("Re: Vl: Kokousmuistio", subject);
    }

    /// <summary>A forward's prefix is its own, and a reply's does not count as one.</summary>
    [Fact]
    public void ForForward_SubjectCarryingAReplyPrefix_TakesTheForwardPrefixAsWell()
    {
        // Act
        var subject = ResponseSubject.ForForward("Re: Quarterly report");

        // Assert
        Assert.Equal("Fwd: Re: Quarterly report", subject);
    }

    /// <summary>A forward prefix already there is recognized in the spellings clients write it in.</summary>
    [Theory]
    [InlineData("Fwd: Quarterly report")]
    [InlineData("FW: Quarterly report")]
    [InlineData("Wg: Quarterly report")]
    [InlineData("Doorst: Quarterly report")]
    [InlineData("Tr: Quarterly report")]
    [InlineData("Vl: Kokousmuistio")]
    public void ForForward_SubjectAlreadyCarryingAPrefix_IsLeftAsItWasWritten(string forwardedSubject)
    {
        // Act
        var subject = ResponseSubject.ForForward(forwardedSubject);

        // Assert
        Assert.Equal(forwardedSubject, subject);
    }

    /// <summary>A message that carried no subject is answered with the prefix alone rather than with a trailing space.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForReply_SubjectNobodyWrote_IsThePrefixAlone(string? answeredSubject)
    {
        // Act
        var subject = ResponseSubject.ForReply(answeredSubject);

        // Assert
        Assert.Equal("Re:", subject);
    }

    /// <summary>
    /// The subject comes from somebody else's message, so a control character surviving an encoded word is removed
    /// rather than refused. Refusing would leave a message nobody can answer over a header its recipient never wrote.
    /// </summary>
    [Fact]
    public void ForReply_SubjectCarryingALineBreak_WritesASubjectAHeaderCanCarry()
    {
        // Act
        var subject = ResponseSubject.ForReply("Quarterly\r\nBcc: elsewhere@example.test");

        // Assert
        Assert.Equal("Re: QuarterlyBcc: elsewhere@example.test", subject);
    }

    /// <summary>A prefix is only one where the subject opens with it, not wherever it happens to appear.</summary>
    [Fact]
    public void ForReply_SubjectMentioningAPrefixLaterOn_TakesThePrefixAnyway()
    {
        // Act
        var subject = ResponseSubject.ForReply("Quarterly report Re: last year");

        // Assert
        Assert.Equal("Re: Quarterly report Re: last year", subject);
    }
}
