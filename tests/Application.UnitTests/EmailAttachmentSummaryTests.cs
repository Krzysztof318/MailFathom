// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests;

public sealed class EmailAttachmentSummaryTests
{
    /// <summary>The totals are computed once, so the list they describe must not be writable behind their back.</summary>
    [Fact]
    public void Create_AttachmentList_CannotBeMutatedThroughTheExposedCollection()
    {
        // Arrange
        AttachmentFileName.TryNormalize("invoice.pdf", out var fileName);
        var summary = EmailAttachmentSummary.Create(
            [new ExtractedEmailAttachment(fileName, "application/pdf", 1024)],
            inlineResourceCount: 0,
            isEncrypted: false,
            carriesUnverifiedSignature: false,
            containsUnexpandedTnefPart: false);

        // Act
        var exposedAsWritableArray = summary.Attachments as ExtractedEmailAttachment[];

        // Assert
        Assert.Null(exposedAsWritableArray);
        Assert.Equal(1024, summary.TotalSizeOctets);
        Assert.Equal("invoice.pdf", Assert.Single(summary.Attachments).FileName?.Value);
    }

    /// <summary>The totals summarize the list rather than being supplied beside it, so they cannot disagree with it.</summary>
    [Fact]
    public void Create_SeveralAttachments_SumsTheirDecodedSizes()
    {
        // Arrange
        var attachments = Enumerable.Range(1, 3)
            .Select(index => new ExtractedEmailAttachment(FileName: null, "application/octet-stream", index * 100));

        // Act
        var summary = EmailAttachmentSummary.Create(
            attachments,
            inlineResourceCount: 2,
            isEncrypted: false,
            carriesUnverifiedSignature: false,
            containsUnexpandedTnefPart: false);

        // Assert
        Assert.Equal(3, summary.AttachmentCount);
        Assert.Equal(600, summary.TotalSizeOctets);
        Assert.Equal(2, summary.InlineResourceCount);
        Assert.True(summary.HasAttachments);
    }

    /// <summary>A message carrying only a body reports the shared empty summary rather than a built one.</summary>
    [Fact]
    public void None_MessageWithNothingBesidesItsBody_ReportsNoAttachments()
    {
        // Assert
        Assert.Empty(EmailAttachmentSummary.None.Attachments);
        Assert.False(EmailAttachmentSummary.None.HasAttachments);
        Assert.Equal(0, EmailAttachmentSummary.None.TotalSizeOctets);
        Assert.Null(EmailAttachmentSummary.None.Attachments as ExtractedEmailAttachment[]);
    }
}
