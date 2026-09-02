// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Authoring;

/// <summary>
/// Covers authoring the two sends that begin from mail this deployment already holds: where each of them is addressed,
/// how it is threaded, what subject it takes, what it quotes, which files a forward carries, and everything it is
/// refused for.
/// </summary>
public sealed class StoredEmailResponseAuthoringTests
{
    private const string SendingAddress = "mailfathom@example.test";

    private static readonly byte[] StoredRawMime = Encoding.UTF8.GetBytes("From: sender@example.test\r\n\r\nBody");

    private static readonly DateTimeOffset SentAt = new(2026, 8, 17, 9, 30, 0, TimeSpan.Zero);

    /// <summary>A reply goes where the message asked for answers to go, which is the header the sender wrote it in.</summary>
    [Fact]
    public async Task AuthorAsync_ReplyToAMessageWritingAReplyToHeader_AddressesThatHeaderRatherThanTheAuthor()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(participants:
        [
            Participant(EmailAddressRole.From, "author@example.test"),
            Participant(EmailAddressRole.ReplyTo, "desk@example.test"),
        ]));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["desk@example.test"], Addressed(response, OutgoingRecipientRole.To));
    }

    /// <summary>Where the message named no such header, a reply goes to whoever wrote it.</summary>
    [Fact]
    public async Task AuthorAsync_ReplyToAMessageWithoutAReplyToHeader_AddressesItsAuthor()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering());

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["author@example.test"], Addressed(response, OutgoingRecipientRole.To));
    }

    /// <summary>A reply answers one mailbox and leaves everybody else the message was between out of it.</summary>
    [Fact]
    public async Task AuthorAsync_Reply_LeavesTheOtherRecipientsOfTheMessageOut()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(participants: ExchangeNamingTheAccount()));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["author@example.test"], Addressed(response, OutgoingRecipientRole.To));
        Assert.Empty(Addressed(response, OutgoingRecipientRole.Cc));
    }

    /// <summary>
    /// A reply to all keeps everybody in the conversation, in the header each of them was named in, and removes the
    /// mailboxes the sending account owns from both. A system that mails the account back has written a loop.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ReplyToAllOfAMessageNamingTheAccount_ExcludesTheAccountsOwnAddressFromBothHeaders()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(participants: ExchangeNamingTheAccount()));

        // Act
        var response = await authoring.AuthorAsync(
            Request(AuthoredResponseAct.ReplyToAll),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["author@example.test", "colleague@example.test"],
            Addressed(response, OutgoingRecipientRole.To));
        Assert.Equal(["watcher@example.test"], Addressed(response, OutgoingRecipientRole.Cc));
    }

    /// <summary>
    /// One mailbox is offered once, and in the most visible header it was named in. A message copying its own author is
    /// ordinary, and answering it must not address them twice.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ReplyToAllOfAMessageNamingAMailboxTwice_OffersItOnce()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(participants:
        [
            Participant(EmailAddressRole.From, "author@example.test"),
            Participant(EmailAddressRole.Cc, "author@example.test"),
            Participant(EmailAddressRole.To, "colleague@example.test"),
        ]));

        // Act
        var response = await authoring.AuthorAsync(
            Request(AuthoredResponseAct.ReplyToAll),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["author@example.test", "colleague@example.test"],
            Addressed(response, OutgoingRecipientRole.To));
        Assert.Empty(Addressed(response, OutgoingRecipientRole.Cc));
    }

    /// <summary>
    /// Whoever asked for answers is who an answer goes to, even where that is this account's own address — a message
    /// somebody sent themselves and a shared mailbox two colleagues both send as both look like that. Leaving it out
    /// would resolve the reply to nobody and refuse it, which is worse than the answer a mail client gives.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ReplyToAMessageWrittenFromTheAccountsOwnAddress_StillAddressesIt()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(participants:
        [
            Participant(EmailAddressRole.From, SendingAddress),
            Participant(EmailAddressRole.To, "colleague@example.test"),
        ]));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([SendingAddress], Addressed(response, OutgoingRecipientRole.To));
    }

    /// <summary>A forward goes to the people its author named and to nobody the original was between.</summary>
    [Fact]
    public async Task AuthorAsync_Forward_AddressesOnlyThePeopleItsAuthorNamed()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(participants: ExchangeNamingTheAccount()));
        var request = Request(AuthoredResponseAct.Forward) with
        {
            Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.To, "elsewhere@example.test")],
        };

        // Act
        var response = await authoring.AuthorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["elsewhere@example.test"], Addressed(response, OutgoingRecipientRole.To));
    }

    /// <summary>
    /// Somebody the author copies in may be named out of the contact book, and is an ordinary address by the time the
    /// answer is composed — with the contact recorded beside it.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_AuthorCopiesInAContact_AddressesTheAddressThatContactPrefers()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var anna = ContactHeldBy(book, "Anna Kowalska", "anna@example.test");
        var authoring = AuthoringOver(Rendering(), contacts: book);
        var request = Request() with
        {
            Recipients = [NamedRecipient.ByContact(OutgoingRecipientRole.Cc, anna.Id)],
        };

        // Act
        var response = await authoring.AuthorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["anna@example.test"], Addressed(response, OutgoingRecipientRole.Cc));
        Assert.Equal(
            anna.Id,
            Assert.Single(response.Email!.Recipients, recipient => recipient.Contact is not null).Contact);
    }

    /// <summary>
    /// A name several people carry addresses nobody, and the answer is refused as a whole rather than sent to the
    /// people whose names were unambiguous.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_AuthorNamesAContactSeveralPeopleCarry_IsRefusedNamingHowManyMatched()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        ContactHeldBy(book, "Anna Kowalska", "anna@example.test");
        ContactHeldBy(book, "Anna Kowalska", "anna.k@example.test");
        var authoring = AuthoringOver(Rendering(), contacts: book);
        var request = Request() with
        {
            Recipients =
            [
                NamedRecipient.ByContactName(
                    OutgoingRecipientRole.Cc,
                    ContactDisplayName.Create("Anna Kowalska")),
            ],
        };

        // Act
        var response = await authoring.AuthorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.IsAuthored);
        Assert.Equal(
            AuthoredResponseRefusalReason.RecipientContactNameAmbiguous,
            response.Refusal?.Reason);
        Assert.Equal(2, response.Refusal?.MatchedContactCount);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailContactNameAmbiguous, response.Refusal?.Failure);
    }

    /// <summary>
    /// The threading headers are the answered message's own identity and the path it carried, which is the whole of
    /// what every client threads by. A guessed value puts the reply in a conversation of its own in every mailbox.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ReplyToAMessageCarryingReferences_ThreadsFromItsOwnHeaders()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(references: EmailThreadReferences.Create(
            "parent@example.test",
            "root@example.test",
            ["root@example.test"])));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        var threading = response.Email!.Threading;
        Assert.Equal("parent@example.test", threading.InReplyTo);
        Assert.Equal(["root@example.test", "parent@example.test"], threading.References);
    }

    /// <summary>The message that started a conversation carries no path, so the reply's path is that message alone.</summary>
    [Fact]
    public async Task AuthorAsync_ReplyToAMessageWithoutReferences_ThreadsFromItsIdentityAlone()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(references: EmailThreadReferences.Create(
            "root@example.test",
            inReplyTo: null,
            references: null)));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        var threading = response.Email!.Threading;
        Assert.Equal("root@example.test", threading.InReplyTo);
        Assert.Equal(["root@example.test"], threading.References);
    }

    /// <summary>The conventional prefix is written once, whichever client wrote the one already there.</summary>
    [Theory]
    [InlineData("Quarterly report", "Re: Quarterly report")]
    [InlineData("Re: Quarterly report", "Re: Quarterly report")]
    [InlineData("Aw: Quarterly report", "Aw: Quarterly report")]
    public async Task AuthorAsync_Reply_TakesTheConventionalPrefixOnlyWhereTheMessageCarriesNone(
        string answeredSubject,
        string expectedSubject)
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(subject: answeredSubject));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedSubject, response.Email!.Subject);
    }

    /// <summary>A forward takes its own prefix, which a reply's does not stand in for.</summary>
    [Fact]
    public async Task AuthorAsync_Forward_TakesTheForwardPrefix()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(subject: "Quarterly report"));

        // Act
        var response = await authoring.AuthorAsync(
            Request(AuthoredResponseAct.Forward),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Fwd: Quarterly report", response.Email!.Subject);
    }

    /// <summary>
    /// The quotation is produced from the stored copy that was read for this answer, with an attribution line above it
    /// and the author's own words above that.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_Reply_QuotesTheAnsweredMessageBeneathWhatItsAuthorWrote()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(plainText: "The report is attached.\nRegards"));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        var body = response.Email!.PlainTextBody;
        Assert.StartsWith("Thank you.", body, StringComparison.Ordinal);
        Assert.Contains("On 2026-08-17 09:30 UTC, author@example.test wrote:", body, StringComparison.Ordinal);
        Assert.Contains("> The report is attached.\n> Regards", body, StringComparison.Ordinal);
    }

    /// <summary>An author who wrote markup gets the answered message quoted in it as well, from the same reading.</summary>
    [Fact]
    public async Task AuthorAsync_ReplyWhoseAuthorWroteMarkup_QuotesTheAnsweredMarkupInTheAlternative()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(html: "<p>The report is attached.</p>"));
        var request = Request() with { HtmlBody = "<p>Thank you.</p>" };

        // Act
        var response = await authoring.AuthorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var html = response.Email!.HtmlBody;
        Assert.NotNull(html);
        Assert.StartsWith("<p>Thank you.</p>", html, StringComparison.Ordinal);
        Assert.Contains("<blockquote><p>The report is attached.</p></blockquote>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each quoted representation is bounded on its own. A rendering spends the read's budget on the plain text before
    /// it reaches the markup, so an answer that handed both the same number would quote an ordinary original in full as
    /// text and leave the markup alternative whatever was left, which is next to nothing.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ReplyWhoseAuthorWroteMarkup_LeavesTheMarkupQuotationItsWholeAllowance()
    {
        // Arrange
        EmailContentRenderingBounds? asked = null;
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Do<EmailContentRenderingBounds>(bounds => asked = bounds),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(Rendering(html: "<p>Attached.</p>"))));
        var authoring = AuthoringOver(Rendering(), renderer: renderer);
        var request = Request() with { HtmlBody = "<p>Thank you.</p>" };

        // Act
        await authoring.AuthorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(asked);
        Assert.True(asked.IncludeSanitizedHtml);
        Assert.True(
            asked.RemainingCharactersForRead - asked.MaxCharactersPerRepresentation
                >= asked.MaxCharactersPerRepresentation,
            "the markup pass keeps its whole allowance after the plain-text pass has spent all of its own");
    }

    /// <summary>An author who wrote plain text alone sends plain text alone, as they would for a message answering nothing.</summary>
    [Fact]
    public async Task AuthorAsync_ReplyWhoseAuthorWroteNoMarkup_CarriesNoAlternative()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(html: "<p>The report is attached.</p>"));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(response.Email!.HtmlBody);
    }

    /// <summary>
    /// The author's own words are never cut. Where the two together exceed what this deployment composes, the quoted
    /// history is what gives way — an author is never told their first paragraph was dropped.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ReplyWhoseAuthorFillsTheWholeBodyBound_KeepsTheirWordsAndQuotesNothing()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(), bounds: Bounds(maxBodyCharacters: 48));
        var request = Request() with { PlainTextBody = new string('a', 48) };

        // Act
        var response = await authoring.AuthorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new string('a', 48), response.Email!.PlainTextBody);
    }

    /// <summary>
    /// A forward carries the original's files out of the local copy, because rebuilding them means either fetching the
    /// message again — which a send has no business doing — or inventing files from their descriptions.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_Forward_CarriesTheFilesOutOfTheStoredCopy()
    {
        // Arrange
        var authoring = AuthoringOver(
            Rendering(attachments: [Description("invoice.pdf", sizeOctets: 8)]),
            attachmentContentReader: ContentReaderOpening("invoice.pdf", "invoice"));

        // Act
        var response = await authoring.AuthorAsync(
            Request(AuthoredResponseAct.Forward),
            TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(response.Email!.Attachments);
        Assert.Equal("invoice.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal("invoice", Encoding.UTF8.GetString(attachment.Content.Span));
    }

    /// <summary>A reply carries none of the answered message's files, because a reply is not a copy of it.</summary>
    [Fact]
    public async Task AuthorAsync_Reply_CarriesNoneOfTheAnsweredMessagesFiles()
    {
        // Arrange
        var contentReader = ContentReaderOpening("invoice.pdf", "invoice");
        var authoring = AuthoringOver(
            Rendering(attachments: [Description("invoice.pdf", sizeOctets: 8)]),
            attachmentContentReader: contentReader);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(response.Email!.Attachments);
        await contentReader
            .DidNotReceive()
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The files belong to the original rather than to whoever forwards it, so the answer is the only place their
    /// number and size can be judged. Each bound is measured before an octet of one file is read.
    /// </summary>
    [Theory]
    [InlineData(4, 8, 3)]
    [InlineData(1, 512, 128)]
    [InlineData(3, 128, 300)]
    public async Task AuthorAsync_ForwardWhoseFilesExceedABound_IsRefusedNamingTheLimit(
        int fileCount,
        long fileSizeOctets,
        long expectedBound)
    {
        // Arrange
        var descriptions = Enumerable
            .Range(0, fileCount)
            .Select(position => Description($"file-{position}.pdf", fileSizeOctets))
            .ToArray();
        var contentReader = ContentReaderOpening("invoice.pdf", "invoice");
        var authoring = AuthoringOver(
            Rendering(attachments: descriptions),
            attachmentContentReader: contentReader,
            bounds: Bounds());

        // Act
        var response = await authoring.AuthorAsync(
            Request(AuthoredResponseAct.Forward),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(response, AuthoredResponseRefusalReason.BoundExceeded, MailFathomErrorCode.OutgoingEmailBoundExceeded);
        Assert.Equal(expectedBound, response.Refusal!.Bound);
        await contentReader
            .DidNotReceive()
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A folder an operator withheld from tools is outside every mailbox read, and a reply must not become the path by
    /// which its content leaves. The refusal is the same one a read of that email gives.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_EmailOfAFolderWithheldFromTools_IsRefusedAsNoSuchEmail()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(), folderParticipation: StubMailFolderParticipation.Nothing);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.AnsweredEmailNotFound,
            MailFathomErrorCode.AnsweredEmailNotFound);
    }

    /// <summary>
    /// Mail in an account another owner owns is refused identically, so answering it is not a way to read what is in
    /// it: the quotation a reply would carry is the message itself.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_EmailOfAnAccountTheCallersOwnerDoesNotOwn_IsRefusedAsNoSuchEmail()
    {
        // Arrange
        var authoring = AuthoringOver(
            Rendering(),
            authorization: AccessAuthorizations.ForOwnerGranted(
                SyntheticMailOwner.Another,
                MailFathomPermission.MailRead));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.AnsweredEmailNotFound,
            MailFathomErrorCode.AnsweredEmailNotFound);
    }

    /// <summary>An identity this deployment holds nothing for is refused identically, so nobody learns what exists by asking.</summary>
    [Fact]
    public async Task AuthorAsync_EmailThisDeploymentDoesNotHold_IsRefusedAsNoSuchEmail()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(), summaryReader: SummaryReaderReturning(summary: null));

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.AnsweredEmailNotFound,
            MailFathomErrorCode.AnsweredEmailNotFound);
    }

    /// <summary>Content synchronization deliberately left unstored is refused rather than answered with an empty quotation.</summary>
    [Fact]
    public async Task AuthorAsync_EmailWhoseContentWasNeverStored_IsRefusedNamingThat()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create() with
        {
            ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit,
        };
        var authoring = AuthoringOver(Rendering(), summary: summary);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable,
            MailFathomErrorCode.AnsweredEmailContentUnavailable);
    }

    /// <summary>
    /// A local copy that has gone missing is a defect of the stored copy rather than of what was being attempted with
    /// it, so it is recorded exactly as reading the message's content records it.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_EmailWhoseStoredCopyIsMissing_IsRefusedAndRecordsARepairRequest()
    {
        // Arrange
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var summary = SyntheticEmailSummaries.Create();
        var authoring = AuthoringOver(
            Rendering(),
            summary: summary,
            contentStore: ContentStoreReturning(storedContent: null),
            repairRequestStore: repairRequests);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable,
            MailFathomErrorCode.AnsweredEmailContentUnavailable);
        var recorded = Assert.Single(repairRequests.Recorded);
        Assert.Equal(summary.StoredEmailId, recorded.StoredEmailId);
        Assert.Equal(EmailContentDefect.Missing, recorded.Defect);
    }

    /// <summary>
    /// A display name is whatever a sender wrote, so an attribution built from one is untrusted input. Both bodies stay
    /// within what this deployment composes regardless, which is what keeps one message's sender from deciding whether
    /// somebody else's answer can be composed at all. A name written out of the characters the markup alternative
    /// encodes is the same input several times its own length once it is markup, so it is bounded by what it costs
    /// there rather than by how the sender wrote it.
    /// </summary>
    [Theory]
    [InlineData('n')]
    [InlineData('&')]
    [InlineData('<')]
    [InlineData('"')]
    public async Task AuthorAsync_ReplyToAMessageWhoseSenderWroteAnEnormousName_StaysWithinTheBodyBound(
        char nameCharacter)
    {
        // Arrange
        Assert.True(EmailAddress.TryCreate(new string(nameCharacter, 4000), "author@example.test", out var address));
        var authoring = AuthoringOver(
            Rendering(participants: [new EmailParticipant(EmailAddressRole.From, address)]),
            bounds: Bounds(maxBodyCharacters: 512));
        var request = Request() with { HtmlBody = "<p>Thank you.</p>" };

        // Act
        var response = await authoring.AuthorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Email!.PlainTextBody.Length <= 512);
        Assert.True(response.Email.HtmlBody!.Length <= 512);
    }

    /// <summary>
    /// A message carrying no markup of its own is quoted as its text, encoded so that nothing a sender wrote decides
    /// the structure of somebody else's reply. The encoding is an expansion, and the text it expands is the answered
    /// message's, so the quotation is cut by what the encoding produces rather than by what the rendering handed over.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ReplyToAMessageWrittenOutOfMarkupCharacters_StaysWithinTheBodyBound()
    {
        // Arrange
        var authoring = AuthoringOver(
            Rendering(plainText: new string('&', 4000)),
            bounds: Bounds(maxBodyCharacters: 512));
        var request = Request() with { HtmlBody = "<p>Thank you.</p>" };

        // Act
        var response = await authoring.AuthorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Email!.HtmlBody!.Length <= 512);
        Assert.Contains("&amp;", response.Email.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>A stored copy that is not what was written down for it is damaged rather than absent, and is recorded as such.</summary>
    [Fact]
    public async Task AuthorAsync_EmailWhoseStoredCopyDoesNotMatchWhatWasRecorded_IsRefusedAndRecordsARepairRequest()
    {
        // Arrange
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var authoring = AuthoringOver(
            Rendering(),
            contentStore: ContentStoreReturning(
                new StoredEmailContent(StoredRawMime, StoredRawMime.Length + 1, SHA256.HashData(StoredRawMime))),
            repairRequestStore: repairRequests);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable,
            MailFathomErrorCode.AnsweredEmailContentUnavailable);
        Assert.Equal(EmailContentDefect.ByteLengthMismatch, Assert.Single(repairRequests.Recorded).Defect);
    }

    /// <summary>
    /// Quoting a message whose object could not be vouched for succeeds from the copy the database kept, and records the
    /// same note a read of it would: which door reached the mail decides nothing about what was found wrong behind it.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ContentServedFromTheRetainedCopy_AnswersAndRecordsARepairRequest()
    {
        // Arrange
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var authoring = AuthoringOver(
            Rendering(),
            contentStore: ContentStoreReturning(IntactContent() with { WasServedFromRetainedCopy = true }),
            repairRequestStore: repairRequests);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response.Email);
        Assert.Equal(EmailContentDefect.ObjectUnreadable, Assert.Single(repairRequests.Recorded).Defect);
    }

    /// <summary>Bytes that arrived intact and no longer parse are a different defect, and one a second fetch may not repair.</summary>
    [Fact]
    public async Task AuthorAsync_EmailWhoseStoredBytesNoLongerParse_IsRefusedAndRecordsARepairRequest()
    {
        // Arrange
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Any<EmailContentRenderingBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Unreadable()));
        var authoring = AuthoringOver(Rendering(), renderer: renderer, repairRequestStore: repairRequests);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable,
            MailFathomErrorCode.AnsweredEmailContentUnavailable);
        Assert.Equal(EmailContentDefect.Unreadable, Assert.Single(repairRequests.Recorded).Defect);
    }

    /// <summary>
    /// A forward whose file stops opening is a damaged copy found halfway through, so it is refused rather than sent
    /// carrying whichever files were read before it.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ForwardWhoseFileCannotBeOpened_IsRefusedAndRecordsARepairRequest()
    {
        // Arrange
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var contentReader = Substitute.For<IEmailAttachmentContentReader>();
        contentReader
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OpenedEmailAttachmentResult.Unreadable()));
        var authoring = AuthoringOver(
            Rendering(attachments: [Description("invoice.pdf", sizeOctets: 8)]),
            attachmentContentReader: contentReader,
            repairRequestStore: repairRequests);

        // Act
        var response = await authoring.AuthorAsync(
            Request(AuthoredResponseAct.Forward),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable,
            MailFathomErrorCode.AnsweredEmailContentUnavailable);
        Assert.Equal(EmailContentDefect.Unreadable, Assert.Single(repairRequests.Recorded).Defect);
    }

    /// <summary>A forward says it is one, so its attribution is not the line a reply writes.</summary>
    [Fact]
    public async Task AuthorAsync_Forward_AttributesTheQuotationAsAForwardedMessage()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering());

        // Act
        var response = await authoring.AuthorAsync(
            Request(AuthoredResponseAct.Forward),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            "Forwarded message from author@example.test, sent 2026-08-17 09:30 UTC:",
            response.Email!.PlainTextBody,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A message naming only part of where it came from is still answerable, and the attribution says what it can
    /// rather than writing a name or a moment nobody wrote.
    /// </summary>
    [Theory]
    [InlineData(AuthoredResponseAct.Reply, false, false, "The answered message read:")]
    [InlineData(AuthoredResponseAct.Reply, false, true, "On 2026-08-17 09:30 UTC, the answered message read:")]
    [InlineData(AuthoredResponseAct.Reply, true, false, "author@example.test wrote:")]
    [InlineData(AuthoredResponseAct.Forward, false, false, "Forwarded message:")]
    [InlineData(AuthoredResponseAct.Forward, false, true, "Forwarded message, sent 2026-08-17 09:30 UTC:")]
    [InlineData(AuthoredResponseAct.Forward, true, false, "Forwarded message from author@example.test:")]
    public async Task AuthorAsync_MessageNamingOnlyPartOfWhereItCameFrom_AttributesWhatItCan(
        AuthoredResponseAct act,
        bool namesItsAuthor,
        bool carriesADate,
        string expectedAttribution)
    {
        // Arrange
        var rendering = Rendering() with
        {
            Headers = new EmailContentHeaders(
                "Quarterly report",
                carriesADate ? SentAt : null,
                ReceivedAt: null,
                namesItsAuthor ? [Participant(EmailAddressRole.From, "author@example.test")] : [],
                EmailThreadReferences.None),
        };
        var authoring = AuthoringOver(rendering);

        // Act
        var response = await authoring.AuthorAsync(Request(act), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(expectedAttribution, response.Email!.PlainTextBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// A body inside a cryptographic envelope is held rather than damaged, so nothing is repaired — and there is still
    /// nothing to quote, which is what makes the answer refused rather than empty.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_EmailWhoseBodyIsEncrypted_IsRefusedAndRecordsNoRepairRequest()
    {
        // Arrange
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var authoring = AuthoringOver(Rendering(encrypted: true), repairRequestStore: repairRequests);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable,
            MailFathomErrorCode.AnsweredEmailContentUnavailable);
        Assert.Empty(repairRequests.Recorded);
    }

    /// <summary>
    /// The sending address is what a reply to all leaves out, so an account configuring none is refused here rather
    /// than composed and mailed its own answer.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_AccountConfiguringNoSendingAddress_IsRefusedNamingTheSender()
    {
        // Arrange
        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        senderIdentities.FindSenderIdentity(Arg.Any<MailAccountId>()).Returns((OutgoingSenderIdentity?)null);
        var authoring = AuthoringOver(Rendering(), senderIdentities: senderIdentities);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(
            response,
            AuthoredResponseRefusalReason.SenderUnconfigured,
            MailFathomErrorCode.OutgoingEmailSenderUnconfigured);
    }

    /// <summary>The account the answer is sent as is the one the answered email was stored from, never a caller's choice.</summary>
    [Fact]
    public async Task AuthorAsync_AuthoredAnswer_IsSentAsTheAccountTheAnsweredEmailBelongsTo()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(accountId: "secondary");
        var authoring = AuthoringOver(Rendering(), summary: summary);

        // Act
        var response = await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.IsAuthored);
        Assert.Equal(summary.AccountId, response.AccountId);
    }

    /// <summary>
    /// The answer quotes the message it answers and a forward carries its files, so anything reaching here without the
    /// grant that reads mail would be reading mail by asking to reply to it.
    /// </summary>
    [Fact]
    public async Task AuthorAsync_ReachedByACallerWithoutTheMailReadGrant_RefusesWithoutReadingAnything()
    {
        // Arrange
        var contentStore = ContentStoreReturning(IntactContent());
        var authoring = AuthoringOver(
            Rendering(),
            contentStore: contentStore,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(async () =>
            await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
        await contentStore
            .DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An entrypoint that never stated what admitted it fails rather than defaulting to permitted.</summary>
    [Fact]
    public async Task AuthorAsync_ReachedUnderNoPrincipal_Refuses()
    {
        // Arrange
        var authoring = AuthoringOver(Rendering(), authorization: AccessAuthorizations.ForPrincipal(principal: null));

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(async () =>
            await authoring.AuthorAsync(Request(), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Answering a message must never fetch one, which is the acceptance criterion the whole content path exists under.
    /// The use case holds no mailbox port, so the guarantee is structural rather than a rule somebody has to keep.
    /// </summary>
    [Fact]
    public void StoredEmailResponseAuthoring_ItsDependencies_IncludeNoMailboxPort()
    {
        // Arrange
        Type[] mailboxPorts =
        [
            typeof(IMailboxSessionFactory),
            typeof(IMailboxSession),
            typeof(IMailboxNotificationSessionFactory),
        ];

        // Act
        var dependencies = typeof(StoredEmailResponseAuthoring)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        // Assert
        Assert.Empty(dependencies.Intersect(mailboxPorts));
    }

    private static void AssertRefused(
        AuthoredResponse response,
        AuthoredResponseRefusalReason reason,
        MailFathomErrorCode failure)
    {
        Assert.False(response.IsAuthored);
        Assert.Null(response.Email);
        Assert.Equal(reason, response.Refusal!.Reason);
        Assert.Equal(failure, response.Refusal.Failure);
    }

    /// <summary>Reads back the addresses one header of the authored answer offers, in the order they were placed.</summary>
    private static IReadOnlyList<string> Addressed(AuthoredResponse response, OutgoingRecipientRole role)
    {
        Assert.True(response.IsAuthored);

        return
        [
            .. response.Email!.Recipients
                .Where(recipient => recipient.Role == role)
                .Select(recipient => recipient.Address),
        ];
    }

    /// <summary>An exchange between three people that names the answering account in both addressed headers.</summary>
    private static IReadOnlyList<EmailParticipant> ExchangeNamingTheAccount() =>
    [
        Participant(EmailAddressRole.From, "author@example.test"),
        Participant(EmailAddressRole.To, "colleague@example.test"),
        Participant(EmailAddressRole.To, SendingAddress),
        Participant(EmailAddressRole.Cc, "watcher@example.test"),
        Participant(EmailAddressRole.Cc, SendingAddress),
    ];

    private static EmailParticipant Participant(EmailAddressRole role, string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        return new EmailParticipant(role, emailAddress);
    }

    /// <summary>Puts one person into the book and answers with them, so a test names the contact it just held.</summary>
    private static Contact ContactHeldBy(InMemoryContactBookStore book, string displayName, string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        var contact = Contact.Create(
            ContactId.Create(Guid.CreateVersion7()),
            ContactDisplayName.Create(displayName),
            [emailAddress],
            emailAddress,
            note: null,
            ContactOrigin.Asserted,
            SentAt,
            SentAt);

        book.Hold(contact);

        return contact;
    }

    private static AuthoredResponseRequest Request(AuthoredResponseAct act = AuthoredResponseAct.Reply) => new()
    {
        AnsweredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        Act = act,
        PlainTextBody = "Thank you.",
    };

    private static EmailContentRendering Rendering(
        string? subject = "Quarterly report",
        IReadOnlyList<EmailParticipant>? participants = null,
        EmailThreadReferences? references = null,
        string plainText = "The report is attached.",
        string? html = null,
        bool encrypted = false,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null)
    {
        var carried = attachments ?? [];

        return new EmailContentRendering(
            new EmailContentHeaders(
                subject,
                SentAt,
                SentAt,
                participants ?? [Participant(EmailAddressRole.From, "author@example.test")],
                references ?? EmailThreadReferences.Create("parent@example.test", inReplyTo: null, references: null)),
            new EmailBodyRepresentation(plainText, plainText.Length, EmailBodyTruncation.None),
            html is null ? null : new EmailBodyRepresentation(html, html.Length, EmailBodyTruncation.None),
            new EmailBodyForms(PlainText: true, Html: html is not null),
            encrypted,
            EmailAttachmentSummary.Create(
                carried,
                inlineResourceCount: 0,
                encrypted,
                carriesUnverifiedSignature: false,
                containsUnexpandedTnefPart: false),
            carried);
    }

    private static ExtractedEmailAttachment Description(string fileName, long sizeOctets) => new(
        AttachmentFileName.TryNormalize(fileName, out var normalized) ? normalized : null,
        "application/pdf",
        sizeOctets);

    private static StoredEmailResponseAuthoring AuthoringOver(
        EmailContentRendering rendering,
        EmailSummary? summary = null,
        IStoredEmailSummaryReader? summaryReader = null,
        IEmailContentStore? contentStore = null,
        IEmailContentRenderer? renderer = null,
        IEmailAttachmentContentReader? attachmentContentReader = null,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        IMailFolderParticipationReader? folderParticipation = null,
        IOutgoingSenderIdentityReader? senderIdentities = null,
        IContactDirectory? contacts = null,
        OutgoingEmailBounds? bounds = null,
        AccessAuthorization? authorization = null)
    {
        var answered = summary ?? SyntheticEmailSummaries.Create();

        // One authorization, for the reason AuthoredSendGovernors.Governing states: the mailboxes the scope is resolved
        // against, the book the recipients are resolved out of, and the caller the authoring runs for are one scoped
        // instance in production.
        var callerAuthorization = authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);

        return new StoredEmailResponseAuthoring(
            summaryReader ?? SummaryReaderReturning(answered),
            contentStore ?? ContentStoreReturning(IntactContent()),
            renderer ?? RendererReturning(rendering),
            attachmentContentReader ?? Substitute.For<IEmailAttachmentContentReader>(),
            repairRequestStore ?? new RecordingEmailContentRepairRequestStore(),
            new MailboxScopeResolver(
                OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(answered.AccountId)),
                folderParticipation ?? StubMailFolderParticipation.Mapping(
                    new MailFolderIdentity(answered.AccountId, answered.FolderAlias)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            senderIdentities ?? SenderIdentitiesFor(answered.AccountId),
            new NamedRecipientResolver(
                contacts ?? new InMemoryContactBookStore(),
                ContactBookOwnerships.For(callerAuthorization)),
            bounds ?? Bounds(),
            callerAuthorization);
    }

    private static IStoredEmailSummaryReader SummaryReaderReturning(EmailSummary? summary)
    {
        var reader = Substitute.For<IStoredEmailSummaryReader>();
        reader.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(summary));

        return reader;
    }

    private static IEmailContentStore ContentStoreReturning(StoredEmailContent? storedContent)
    {
        var contentStore = ContentStores.Substituted();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(storedContent));

        return contentStore;
    }

    private static IEmailContentRenderer RendererReturning(EmailContentRendering rendering)
    {
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Any<EmailContentRenderingBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(rendering)));

        return renderer;
    }

    private static IEmailAttachmentContentReader ContentReaderOpening(string fileName, string content)
    {
        var contentReader = Substitute.For<IEmailAttachmentContentReader>();
        contentReader
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(
                OpenedEmailAttachmentResult.Opened(new StubOpenedEmailAttachment(fileName, content))));

        return contentReader;
    }

    private static IOutgoingSenderIdentityReader SenderIdentitiesFor(MailAccountId accountId)
    {
        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        Assert.True(EmailAddress.TryCreate("MailFathom", SendingAddress, out var address));
        senderIdentities
            .FindSenderIdentity(Arg.Any<MailAccountId>())
            .Returns(OutgoingSenderIdentity.Create(accountId, address));

        return senderIdentities;
    }

    private static OutgoingEmailBounds Bounds(int maxBodyCharacters = 4096) => new()
    {
        MaxRecipientCount = 8,
        MaxBodyCharacters = maxBodyCharacters,
        MaxAttachmentCount = 3,
        MaxAttachmentBytes = 128,
        MaxMessageBytes = 300,
    };

    private static StoredEmailContent IntactContent() =>
        new(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime));

    /// <summary>An opened attachment that writes the octets a test named, which is what a forward has to carry.</summary>
    private sealed class StubOpenedEmailAttachment(string fileName, string content) : IOpenedEmailAttachment
    {
        public ExtractedEmailAttachment Description { get; } = new(
            AttachmentFileName.TryNormalize(fileName, out var normalized) ? normalized : null,
            "application/pdf",
            Encoding.UTF8.GetByteCount(content));

        public Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destination);

            return destination.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken).AsTask();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
