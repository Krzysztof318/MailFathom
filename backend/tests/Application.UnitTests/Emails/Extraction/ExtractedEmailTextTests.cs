// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Extraction;

/// <summary>Covers the searchable text a message's body yielded, and what redacting it may and may not change.</summary>
public sealed class ExtractedEmailTextTests
{
    /// <summary>Redaction changes what the words are, never which part of the message they were read from.</summary>
    [Fact]
    public void WithRedactedText_ALossyReadingOfAnHtmlBody_KeepsTheSourceItWasReadFrom()
    {
        // Arrange
        var extracted = ExtractedEmailText.DerivedFromHtmlBody("original", "trimmed");

        // Act
        var redacted = extracted.WithRedactedText("original [redacted:CloudKey]", "trimmed [redacted:CloudKey]");

        // Assert
        Assert.Equal(ExtractedEmailTextSource.DerivedFromHtmlBodyPart, redacted.Source);
        Assert.Equal("original [redacted:CloudKey]", redacted.OriginalText);
        Assert.Equal("trimmed [redacted:CloudKey]", redacted.TrimmedText);
        Assert.True(redacted.HasText);
    }

    /// <summary>A body that yielded no words has nothing to replace, and inventing text for one would be a lie about it.</summary>
    [Theory]
    [InlineData(ExtractedEmailTextSource.NoTextualBodyPart)]
    [InlineData(ExtractedEmailTextSource.EncryptedBody)]
    public void WithRedactedText_AMessageThatYieldedNoWords_IsRefused(ExtractedEmailTextSource source)
    {
        // Arrange
        var extracted = source == ExtractedEmailTextSource.EncryptedBody
            ? ExtractedEmailText.EncryptedBody
            : ExtractedEmailText.NoTextualBody;

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            extracted.WithRedactedText("original", "trimmed"));

        // Assert
        Assert.Contains("nothing to redact", refusal.Message, StringComparison.Ordinal);
    }
}
