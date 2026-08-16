// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Contacts;

public sealed class ContactNoteTests
{
    /// <summary>A note about a person runs to more than one line, so line breaks and tabs survive it.</summary>
    [Fact]
    public void Create_NoteSpanningLines_KeepsTheLayoutTheOwnerWrote()
    {
        // Arrange
        const string written = "Met at the conference.\nOwes an answer about the contract.\n\tDeadline in March.";

        // Act
        var note = ContactNote.Create(written);

        // Assert
        Assert.Equal(written, note.Value);
        Assert.Equal(written, note.ToString());
    }

    /// <summary>Every control character that is not layout is refused, for the reason a name refuses all of them.</summary>
    [Theory]
    [InlineData("Owes \u0007 an answer")]
    [InlineData("Owes \u001b[31m an answer")]
    [InlineData("Owes \0 an answer")]
    public void Create_NoteCarryingATerminalControlCharacter_IsRefused(string written)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactNote.Create(written));
    }

    /// <summary>The layout exception is the two line breaks and the tab, not every character that renders as nothing.</summary>
    [Theory]
    [InlineData("Owes \u2028 an answer")]
    [InlineData("Owes \u202e an answer")]
    [InlineData("Owes \u200b an answer")]
    public void Create_NoteCarryingACharacterThatRendersAsNothing_IsRefused(string written)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactNote.Create(written));
    }

    /// <summary>A note is written in whatever script the owner writes in, joiners included.</summary>
    [Fact]
    public void Create_NoteJoiningItsLettersWithAZeroWidthJoiner_IsKeptAsWritten()
    {
        // Arrange
        const string written = "Signs their mail \u0645\u06cc\u200c\u062e\u0648\u0627\u0647\u0645.";

        // Act
        var note = ContactNote.Create(written);

        // Assert
        Assert.Equal(written, note.Value);
    }

    /// <summary>A contact without a note holds none, so blank text cannot become a second way to say the same absence.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  \n  ")]
    public void Create_BlankNote_IsRefused(string written)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactNote.Create(written));
    }

    /// <summary>The bound keeps the field a note rather than a place to keep documents.</summary>
    [Fact]
    public void Create_NoteAtTheBound_IsAcceptedAndOneCharacterLongerIsRefused()
    {
        // Arrange
        var atBound = new string('a', ContactNote.MaximumLength);
        var overBound = new string('a', ContactNote.MaximumLength + 1);

        // Act
        var accepted = ContactNote.Create(atBound);

        // Assert
        Assert.Equal(atBound, accepted.Value);
        Assert.Throws<ArgumentException>(() => ContactNote.Create(overBound));
    }
}
