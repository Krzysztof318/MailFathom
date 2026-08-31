// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AllocationBudgets.UnitTests;

/// <summary>What extracting one message's metadata out of raw MIME may allocate.</summary>
/// <remarks>
/// This is the path every synchronized message takes, so a regression here is multiplied by the size of a mailbox
/// before anybody sees it. What the budget defends is the decision recorded on the reader itself: the structural pass
/// and the parse both read the stored bytes in place, and the parse is persistent so no part's content is copied into
/// buffers the parser owns.
/// </remarks>
public sealed class MimeParsingAllocationBudgetTests
{
    /// <summary>What one extraction may allocate, as a share of the message it reads.</summary>
    /// <remarks>
    /// A pass that stopped streaming would hold the message a second time and allocate at least its whole length, so
    /// any share below one fails that regression. A quarter is where it is set because the honest fixed cost of an
    /// extraction — MimeKit's read buffers, the object tree, the header strings, and the extracted text bounded at
    /// <see cref="EmailMimeExtractionOptions.MaxExtractedTextCharacters" /> — does not grow with the payload, so on a
    /// message of several megabytes it sits far below this and leaves room for a runtime that sizes a buffer
    /// differently.
    /// </remarks>
    private const double MaximumAllocatedShareOfMessage = 0.25;

    /// <summary>Extraction reads a multi-megabyte message without ever holding a second copy of it.</summary>
    [Fact]
    public async Task ReadMetadataAsync_LargeMessage_StaysWithinItsAllocationBudget()
    {
        // Arrange
        var reader = new MimeKitEmailMimeReader(
            new EmailMimeExtractionOptions(),
            new NoTrustedAuthentication(),
            localSenderVerifier: null);

        var content = LargeSyntheticMessage.AsFetched();
        var cancellationToken = TestContext.Current.CancellationToken;
        var budgetBytes = (long)(content.RawMime.Length * MaximumAllocatedShareOfMessage);

        // The measured run asserts nothing, because an assertion inside it would allocate and be charged to the path.
        // Establishing that the run does the work is therefore a step of its own, before anything is counted.
        var extraction = await reader.ReadMetadataAsync(content, SyntheticMailOwner.Deployment, cancellationToken);
        Assert.Equal(EmailMimeExtractionOutcome.Extracted, extraction.Outcome);

        // Act, Assert
        await AllocationBudget.AssertWithinAsync(
            "Extracting metadata from a large message",
            budgetBytes,
            () => reader.ReadMetadataAsync(content, SyntheticMailOwner.Deployment, cancellationToken));
    }
}
