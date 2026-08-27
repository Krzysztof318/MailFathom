// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Configuration;
using Xunit;

namespace MailFathom.Application.UnitTests.Configuration;

/// <summary>
/// Covers the one shape a change to the deployment's settings can take: a configuration path, and either a value or
/// the decision to stop carrying one. What the guards refuse is a path that names no setting, which is a caller's
/// mistake rather than a write an operator can correct.
/// </summary>
public sealed class ConfigurationEditTests
{
    /// <summary>A change that sets a value carries it, and says it is not a removal.</summary>
    [Fact]
    public void SetTo_AValue_CarriesThePathAndTheValue()
    {
        // Act
        var edit = ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3");

        // Assert
        Assert.Equal("MailboxSearch:SnippetsPerEmail", edit.Path);
        Assert.Equal("3", edit.Value);
        Assert.False(edit.RemovesTheSetting);
    }

    /// <summary>An empty value is a value an operator can mean, so it is carried rather than read as a removal.</summary>
    [Fact]
    public void SetTo_AnEmptyValue_IsAValueRatherThanARemoval()
    {
        // Act
        var edit = ConfigurationEdit.SetTo("Deployment:PublicBaseAddress", string.Empty);

        // Assert
        Assert.Equal(string.Empty, edit.Value);
        Assert.False(edit.RemovesTheSetting);
    }

    /// <summary>A removal carries no value, which is what makes the setting inherited from the source beneath the layer.</summary>
    [Fact]
    public void Removing_APath_CarriesNoValue()
    {
        // Act
        var edit = ConfigurationEdit.Removing("MailboxSearch:SnippetsPerEmail");

        // Assert
        Assert.Equal("MailboxSearch:SnippetsPerEmail", edit.Path);
        Assert.Null(edit.Value);
        Assert.True(edit.RemovesTheSetting);
    }

    /// <summary>An index is an ordinary path segment, because that is how the persisted document addresses one.</summary>
    [Fact]
    public void SetTo_AnIndexedElement_IsAnOrdinaryPath()
    {
        // Act
        var edit = ConfigurationEdit.SetTo("MailRules:Rules:1:Name", "second");

        // Assert
        Assert.Equal("MailRules:Rules:1:Name", edit.Path);
    }

    /// <summary>A path with an empty segment addresses nothing, whichever end of the key the segment is at.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":Leading")]
    [InlineData("Trailing:")]
    [InlineData("Two::Colons")]
    [InlineData("White: :Space")]
    public void SetTo_APathThatNamesNoSetting_IsRefused(string path)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => ConfigurationEdit.SetTo(path, "value"));
        Assert.Throws<ArgumentException>(() => ConfigurationEdit.Removing(path));
    }

    /// <summary>A null value is a removal rather than a value, so it is refused where a value is asked for.</summary>
    [Fact]
    public void SetTo_ANullValue_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", null!));
    }

    /// <summary>A value past the bound is refused where it is stated, rather than expanded into a candidate document first.</summary>
    [Fact]
    public void SetTo_AValuePastTheBound_IsRefused()
    {
        // Arrange
        var oversized = new string('x', ConfigurationEdit.MaximumValueLength + 1);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", oversized));
    }

    /// <summary>A value at the bound is a value, because a ceiling an operator cannot reach is a ceiling stated wrongly.</summary>
    [Fact]
    public void SetTo_AValueAtTheBound_IsCarried()
    {
        // Arrange
        var longest = new string('x', ConfigurationEdit.MaximumValueLength);

        // Act
        var edit = ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", longest);

        // Assert
        Assert.Equal(longest, edit.Value);
    }

    /// <summary>
    /// A NUL composes into valid JSON and can never be stored, so it is the one value refused for what the store would
    /// do with it rather than for its shape. Left to the commit it would compose, validate, and be refused by the
    /// server on every attempt, with nothing but a state number to tell the operator which value did it.
    /// </summary>
    [Fact]
    public void SetTo_AValueCarryingANulCharacter_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(
            () => ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "three\0four"));

        // Assert
        Assert.Contains("NUL", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same edit, and neither guard above it sees the character: a NUL is not white space, and a
    /// segment carrying one is not empty. A segment becomes a JSON property name, and PostgreSQL refuses a NUL in a key
    /// exactly as it refuses one in a string.
    /// </summary>
    [Fact]
    public void SetTo_APathCarryingANulCharacter_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(
            () => ConfigurationEdit.SetTo("Mailbox\0Search:SnippetsPerEmail", "3"));

        // Assert
        Assert.Contains("NUL", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A removal states a path and no value, so the path is the only half there is to refuse.</summary>
    [Fact]
    public void Removing_APathCarryingANulCharacter_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(
            () => ConfigurationEdit.Removing("Mailbox\0Search:SnippetsPerEmail"));

        // Assert
        Assert.Contains("NUL", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record renders every member it holds into every log line and diagnostic dump the value reaches, and the value
    /// here is whatever an operator stated — a credential wherever the setting is one. What identifies the change is
    /// printed; what would disclose it is not.
    /// </summary>
    [Fact]
    public void ToString_AChangeSettingAValue_PrintsThePathAndNotTheValue()
    {
        // Arrange
        var edit = ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "s3cret-looking-value");

        // Act
        var printed = edit.ToString();

        // Assert
        Assert.Contains("MailboxSearch:SnippetsPerEmail", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-looking-value", printed, StringComparison.Ordinal);
    }

    /// <summary>A removal has no value to withhold, and what it is has to survive the printing.</summary>
    [Fact]
    public void ToString_ARemoval_SaysThatItRemovesTheSetting()
    {
        // Arrange
        var edit = ConfigurationEdit.Removing("MailboxSearch:SnippetsPerEmail");

        // Act
        var printed = edit.ToString();

        // Assert
        Assert.Contains("MailboxSearch:SnippetsPerEmail", printed, StringComparison.Ordinal);
        Assert.Contains("True", printed, StringComparison.Ordinal);
    }

    /// <summary>A path past the bound is refused as a caller that has lost track of the path it is asking for.</summary>
    [Fact]
    public void SetTo_APathPastTheBound_IsRefused()
    {
        // Arrange
        var oversized = new string('x', ConfigurationEdit.MaximumPathLength + 1);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationEdit.SetTo(oversized, "value"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationEdit.Removing(oversized));
    }
}
