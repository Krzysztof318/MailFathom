// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Screening;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Screening;

/// <summary>Covers which of a composed message's words are handed to a scan, and in which order.</summary>
public sealed class OutgoingMailTextTests
{
    /// <summary>The subject is screened first, because it is the cheapest way for a message to be refused.</summary>
    [Fact]
    public void ScreenedValues_AMessageCarryingEveryRepresentation_ScreensTheSubjectFirst()
    {
        // Arrange
        var text = new OutgoingMailText("a subject", "a body", "<p>a body</p>");

        // Act
        var screened = text.ScreenedValues;

        // Assert
        Assert.Equal(["a subject", "a body", "<p>a body</p>"], screened);
    }

    /// <summary>A message with no HTML alternative screens two values rather than paying for a scan of nothing.</summary>
    [Fact]
    public void ScreenedValues_AMessageWithNoMarkup_ScreensTheSubjectAndThePlainText()
    {
        // Arrange
        var text = new OutgoingMailText("a subject", "a body", HtmlBody: null);

        // Act
        var screened = text.ScreenedValues;

        // Assert
        Assert.Equal(["a subject", "a body"], screened);
    }

    /// <summary>Empty text carries nothing, so it is dropped rather than costing one analyzer round trip per message.</summary>
    [Fact]
    public void ScreenedValues_AMessageWhoseRepresentationsAreEmpty_ScreensNothing()
    {
        // Arrange
        var text = new OutgoingMailText(string.Empty, string.Empty, string.Empty);

        // Act
        var screened = text.ScreenedValues;

        // Assert
        Assert.Empty(screened);
    }
}
