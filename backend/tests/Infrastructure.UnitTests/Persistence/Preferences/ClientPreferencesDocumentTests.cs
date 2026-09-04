// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Preferences;
using MailFathom.Infrastructure.Persistence.Preferences;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Preferences;

/// <summary>
/// Covers the shape the row holds. It is a contract with the next start rather than with a client, so what these hold
/// is that a document written by this build reads back as what was written, that a key the document does not carry is
/// answered by that preference's own unset value rather than by an empty one, and that a row nothing here wrote is
/// refused rather than read as something.
/// </summary>
public sealed class ClientPreferencesDocumentTests
{
    [Fact]
    public void Render_APersonsPreferences_WritesEveryOneOfThemUnderItsOwnKey()
    {
        // Act
        var document = ClientPreferencesDocument.Render(
            new ClientPreferences(false, ClientThemeChoice.Dark, true, false, true, true));

        // Assert
        Assert.Equal(
            """{"telemetryEnabled":false,"theme":"dark","openMailInTabs":true,"markReadOnOpen":false,"expandWholeThread":true,"embeddedHtmlMessages":true}""",
            document);
    }

    /// <summary>Written whole rather than as a difference from the defaults, so a stored row states what its writer meant however the defaults later move.</summary>
    [Fact]
    public void Render_PreferencesThatAreAllTheUnsetAnswers_StillWritesEveryKey()
    {
        // Act
        var document = ClientPreferencesDocument.Render(ClientPreferences.Unset);

        // Assert
        Assert.Equal(
            """{"telemetryEnabled":true,"theme":"system","openMailInTabs":false,"markReadOnOpen":true,"expandWholeThread":false,"embeddedHtmlMessages":false}""",
            document);
    }

    [Fact]
    public void Parse_ADocumentThisBuildWrote_ReadsBackWhatWasWritten()
    {
        // Arrange
        var chosen = new ClientPreferences(false, ClientThemeChoice.Light, true, false, true, true);

        // Act
        var read = ClientPreferencesDocument.Parse(ClientPreferencesDocument.Render(chosen));

        // Assert
        Assert.Equal(chosen, read);
    }

    /// <summary>The document is sparse, so a build publishing one more preference reads a row written before it existed.</summary>
    [Fact]
    public void Parse_ADocumentCarryingNoKeys_AnswersEveryPreferenceAsUnset()
    {
        // Act
        var read = ClientPreferencesDocument.Parse("{}");

        // Assert
        Assert.Equal(ClientPreferences.Unset, read);
    }

    [Fact]
    public void Parse_ADocumentCarryingOnlyOneKey_AnswersTheOthersAsUnset()
    {
        // Act
        var read = ClientPreferencesDocument.Parse("""{"theme":"dark"}""");

        // Assert
        Assert.Equal(new ClientPreferences(true, ClientThemeChoice.Dark, false, true, false, false), read);
    }

    /// <summary>The reduced text is what the client drew before the message view was a preference, so a row written then reads as that rather than as the sender's own markup.</summary>
    [Fact]
    public void Parse_ARowWrittenBeforeTheMessageViewWasAPreference_AnswersItAsTheReducedText()
    {
        // Act
        var read = ClientPreferencesDocument.Parse(
            """{"telemetryEnabled":false,"theme":"dark","openMailInTabs":true,"markReadOnOpen":true,"expandWholeThread":true}""");

        // Assert
        Assert.False(read.EmbeddedHtmlMessages);
    }

    /// <summary>Opening a conversation at the message it was opened at is what the client did before the preference existed, so a row written then reads as that rather than as expanding.</summary>
    [Fact]
    public void Parse_ARowWrittenBeforeThreadExpansionWasAPreference_AnswersItAsOff()
    {
        // Act
        var read = ClientPreferencesDocument.Parse(
            """{"telemetryEnabled":false,"theme":"dark","openMailInTabs":true,"markReadOnOpen":true}""");

        // Assert
        Assert.False(read.ExpandWholeThread);
    }

    /// <summary>ADR 0026 defaults marking read to on, so a row written before the preference existed reads as marking rather than as declining it.</summary>
    [Fact]
    public void Parse_ARowWrittenBeforeMarkingReadWasAPreference_AnswersItAsOn()
    {
        // Act
        var read = ClientPreferencesDocument.Parse("""{"telemetryEnabled":false,"theme":"dark","openMailInTabs":true}""");

        // Assert
        Assert.True(read.MarkReadOnOpen);
    }

    /// <summary>A key this build does not know is one a later build wrote, and the strict binding that refuses one belongs at the boundary a person writes through.</summary>
    [Fact]
    public void Parse_ADocumentCarryingAKeyThisBuildDoesNotPublish_ReadsTheRestOfIt()
    {
        // Act
        var read = ClientPreferencesDocument.Parse("""{"theme":"light","messageListWidth":320}""");

        // Assert
        Assert.Equal(ClientThemeChoice.Light, read.Theme);
    }

    [Fact]
    public void Parse_ARowNamingAThemeNothingPublishes_Refuses()
    {
        // Assert
        Assert.Throws<JsonException>(() => ClientPreferencesDocument.Parse("""{"theme":"solarized"}"""));
    }

    [Fact]
    public void Parse_ARowThatIsNotAnObject_Refuses()
    {
        // Assert
        Assert.Throws<JsonException>(() => ClientPreferencesDocument.Parse("[]"));
    }

    [Fact]
    public void Parse_ARowHoldingTheNullLiteral_Refuses()
    {
        // Assert
        Assert.Throws<JsonException>(() => ClientPreferencesDocument.Parse("null"));
    }

    [Fact]
    public void Render_NoPreferencesAtAll_IsRefused()
    {
        // Assert
        Assert.Throws<ArgumentNullException>(() => ClientPreferencesDocument.Render(null!));
    }
}
