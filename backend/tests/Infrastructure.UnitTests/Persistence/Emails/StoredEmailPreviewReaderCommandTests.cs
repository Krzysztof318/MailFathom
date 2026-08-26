// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>
/// Covers what a page's previews ask PostgreSQL for, which the C# they are written in does not show. A projection that
/// cut the text in this process would answer exactly the same rows — while having carried every one of those bodies
/// across the boundary the bound exists to keep them behind — so the generated command is what is read rather than the
/// result.
/// </summary>
public sealed class StoredEmailPreviewReaderCommandTests
{
    /// <summary>The bound is the database's to apply, and the command is the only place that claim is visible.</summary>
    [Fact]
    public void PreviewsOf_APageOfEmails_AsksPostgreSqlToCutTheTextToThePublishedBound()
    {
        // Act
        var command = PreviewCommand();

        // Assert
        Assert.Contains("substring(", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            EmailPreview.MaximumCharacters.ToString(CultureInfo.InvariantCulture),
            command,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The untrimmed reading is the column extraction keeps so an over-aggressive cut is recoverable, and a preview
    /// taken from it would open every reply with the quotation it was answering.
    /// </summary>
    [Fact]
    public void PreviewsOf_APageOfEmails_ReadsTheTrimmedTextAndNotTheReadingBesideIt()
    {
        // Act
        var command = PreviewCommand();

        // Assert
        Assert.Contains(nameof(EmailSearchDocumentEntity.BodyText), command, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(EmailSearchDocumentEntity.BodyTextBeforeTrimming),
            command,
            StringComparison.Ordinal);
    }

    /// <summary>The page is what bounds this read, so the identities have to narrow the query rather than a walk of the table.</summary>
    [Fact]
    public void PreviewsOf_APageOfEmails_NarrowsToTheNamedEmailsInTheCommand()
    {
        // Act
        var command = PreviewCommand();

        // Assert
        var narrowing = command[command.IndexOf("WHERE", StringComparison.Ordinal)..];

        Assert.Contains(nameof(EmailSearchDocumentEntity.StoredEmailId), narrowing, StringComparison.Ordinal);
    }

    /// <summary>A message nothing has extracted has no preview, and leaving it out is what keeps an unextracted page one query returning nothing.</summary>
    [Fact]
    public void PreviewsOf_APageOfEmails_LeavesOutTheDocumentsThatCarryNoText()
    {
        // Act
        var command = PreviewCommand();

        // Assert
        var narrowing = command[command.IndexOf("WHERE", StringComparison.Ordinal)..];

        Assert.Contains("IS NOT NULL", narrowing, StringComparison.Ordinal);
    }

    /// <summary>Generates the command, without opening a connection.</summary>
    private static string PreviewCommand()
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return StoredEmailPreviewReader
            .PreviewsOf(context, [StoredEmailId.Create(Guid.CreateVersion7())])
            .ToQueryString();
    }
}
