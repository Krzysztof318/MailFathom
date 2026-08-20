// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Mail.Mime.Composition;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime.Composition;

/// <summary>
/// Covers the one place an outgoing message is built: which headers this system owns rather than a caller, what an
/// authored field may not smuggle into one, which recipients the envelope ends up offering, and every bound a message
/// is refused for before a connection is worth opening.
/// </summary>
public sealed class MimeKitAuthoredEmailComposerTests
{
    /// <summary>A value carrying a line break, which is what every injected header begins with.</summary>
    private const string Injection = "Quarterly report\r\nBcc: elsewhere@example.test";

    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly DateTimeOffset ComposedAt = new(2026, 8, 17, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The contact a resolved recipient came from, which the composition carries and never reads.</summary>
    private static readonly ContactId Contact =
        ContactId.Create(Guid.Parse("0198f0a0-4444-7000-8000-000000000001"));

    /// <summary>The account's own address is what a message is written from, and there is no input path that names one.</summary>
    [Fact]
    public void Compose_AuthoredMessage_WritesTheSendingAccountsOwnAddressAndName()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.Compose(Account, Requester(), Authored(), Capabilities());

        // Assert
        var sender = Assert.IsType<MailboxAddress>(Assert.Single(Parse(composition).From));
        Assert.Equal("mailfathom@example.test", sender.Address);
        Assert.Equal("MailFathom", sender.Name);
    }

    /// <summary>An endpoint configured without an address to send from composes nothing, and says so as itself.</summary>
    [Fact]
    public void Compose_AccountConfiguringNoSendingAddress_IsRefusedNamingTheSender()
    {
        // Arrange
        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        senderIdentities.FindSenderIdentity(Account).Returns((OutgoingSenderIdentity?)null);
        var composer = new MimeKitAuthoredEmailComposer(senderIdentities, Bounds(), new FakeTimeProvider(ComposedAt));

        // Act
        var composition = composer.Compose(Account, Requester(), Authored(), Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.SenderUnconfigured,
            AuthoredEmailField.Sender,
            MailFathomErrorCode.OutgoingEmailSenderUnconfigured);
    }

    /// <summary>The identity is minted in the sending account's domain and is what the transmitted header carries.</summary>
    [Fact]
    public void Compose_AuthoredMessage_MintsAMessageIdInTheSendersDomainAndWritesIt()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.Compose(Account, Requester(), Authored(), Capabilities());

        // Assert
        var messageId = composition.Email!.MessageId;
        Assert.EndsWith("@example.test", messageId.Value, StringComparison.Ordinal);
        Assert.Equal(messageId.Value, Parse(composition).MessageId);
    }

    /// <summary>
    /// Two compositions of one authored message are two messages. That is why a send stores its own once and transmits
    /// the stored bytes: recomposing between attempts would thread one send as two in every recipient's client.
    /// </summary>
    [Fact]
    public void Compose_SameAuthoredMessageTwice_MintsADistinctIdentityEachTime()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var first = composer.Compose(Account, Requester(), Authored(), Capabilities());
        var second = composer.Compose(Account, Requester(), Authored(), Capabilities());

        // Assert
        Assert.NotEqual(first.Email!.MessageId, second.Email!.MessageId);
    }

    /// <summary>The timestamp is the injected clock's, as every timestamp in this system is.</summary>
    [Fact]
    public void Compose_AuthoredMessage_DatesItFromTheInjectedClock()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.Compose(Account, Requester(), Authored(), Capabilities());

        // Assert
        Assert.Equal(ComposedAt, Parse(composition).Date);
    }

    /// <summary>
    /// One mailbox is offered once. Somebody an author named both as a primary recipient and as a blind one was meant to
    /// be seen, so the more visible header keeps them and the later mention is dropped.
    /// </summary>
    [Fact]
    public void Compose_OneMailboxNamedInTwoHeaders_OffersItOnceInTheMoreVisibleOne()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients =
            [
                new AuthoredEmailRecipient(OutgoingRecipientRole.Bcc, "Anna@example.test"),
                new AuthoredEmailRecipient(OutgoingRecipientRole.To, "anna@example.test", "Anna"),
            ],
        };

        // Act
        var composition = composer.Compose(Account, Requester(), authored, Capabilities());

        // Assert
        var recipient = Assert.Single(composition.Email!.Request.Recipients);
        Assert.Equal(OutgoingRecipientRole.To, recipient.Role);
        var message = Parse(composition);
        Assert.Equal("anna@example.test", Assert.IsType<MailboxAddress>(Assert.Single(message.To)).Address);
        Assert.Empty(message.Bcc);
    }

    /// <summary>
    /// A recipient whose address came out of the contact book is recorded with the contact beside the address, so what
    /// was sent stays answerable after the book changes.
    /// </summary>
    [Fact]
    public void Compose_RecipientResolvedFromAContact_RecordsTheContactBesideTheAddress()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients =
            [
                new AuthoredEmailRecipient(OutgoingRecipientRole.To, "anna@example.test", "Anna Kowalska", Contact),
            ],
        };

        // Act
        var composition = composer.Compose(Account, Requester(), authored, Capabilities());

        // Assert
        var recipient = Assert.Single(composition.Email!.Request.Recipients);
        Assert.Equal("anna@example.test", recipient.Address.Address);
        Assert.Equal(Contact, recipient.Contact);
        Assert.Equal(
            "Anna Kowalska",
            Assert.IsType<MailboxAddress>(Assert.Single(Parse(composition).To)).Name);
    }

    /// <summary>
    /// An address the book supplied is an ordinary address from that moment on, so every refusal a written-down address
    /// meets it meets as well. Naming a contact is therefore no way to reach a mailbox naming an address could not.
    /// </summary>
    [Fact]
    public void Compose_ContactResolvedAddressTheServerCannotCarry_IsRefusedLikeAnyOther()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients = [new AuthoredEmailRecipient(OutgoingRecipientRole.To, "zoë@example.test", null, Contact)],
        };

        // Act
        var composition = composer.Compose(
            Account,
            Requester(),
            authored,
            Capabilities(acceptsInternationalizedAddresses: false));

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.InternationalizationUnsupported,
            AuthoredEmailField.To,
            MailFathomErrorCode.OutgoingEmailInternationalizationUnsupported);
    }

    /// <summary>
    /// One mailbox is offered once whether it was named as a person or written down, because the envelope compares the
    /// address and knows nothing about the book.
    /// </summary>
    [Fact]
    public void Compose_AContactAndAWrittenAddressNamingOneMailbox_OffersItOnce()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients =
            [
                new AuthoredEmailRecipient(OutgoingRecipientRole.To, "anna@example.test", "Anna Kowalska", Contact),
                new AuthoredEmailRecipient(OutgoingRecipientRole.Cc, "Anna@Example.test"),
            ],
        };

        // Act
        var composition = composer.Compose(Account, Requester(), authored, Capabilities());

        // Assert
        var recipient = Assert.Single(composition.Email!.Request.Recipients);
        Assert.Equal(OutgoingRecipientRole.To, recipient.Role);
        Assert.Equal(Contact, recipient.Contact);
        Assert.Empty(Parse(composition).Cc);
    }

    /// <summary>A blind recipient is offered exactly as any other is, and the transmitted headers do not name them.</summary>
    [Fact]
    public void Compose_BlindRecipient_IsOfferedButNeverNamedInTheTransmittedHeaders()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients =
            [
                new AuthoredEmailRecipient(OutgoingRecipientRole.To, "anna@example.test"),
                new AuthoredEmailRecipient(OutgoingRecipientRole.Bcc, "auditor@example.test"),
            ],
        };

        // Act
        var composition = composer.Compose(Account, Requester(), authored, Capabilities());

        // Assert
        Assert.Equal(2, composition.Email!.Request.Recipients.Count);
        Assert.Contains(
            composition.Email.Request.Recipients,
            recipient => recipient.Role == OutgoingRecipientRole.Bcc);
        Assert.DoesNotContain(
            "auditor@example.test",
            Encoding.UTF8.GetString(composition.Email.RawMime.Span),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An address that names no mailbox is refused against the header the author wrote it in, never against the address.</summary>
    [Theory]
    [InlineData(OutgoingRecipientRole.To, AuthoredEmailField.To)]
    [InlineData(OutgoingRecipientRole.Cc, AuthoredEmailField.Cc)]
    [InlineData(OutgoingRecipientRole.Bcc, AuthoredEmailField.Bcc)]
    public void Compose_RecipientNamingNoMailbox_IsRefusedNamingTheHeader(
        OutgoingRecipientRole role,
        AuthoredEmailField field)
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients =
            [
                new AuthoredEmailRecipient(OutgoingRecipientRole.To, "anna@example.test"),
                new AuthoredEmailRecipient(role, "not-a-mailbox"),
            ],
        };

        // Act
        var composition = composer.Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.FieldUnusable,
            field,
            MailFathomErrorCode.OutgoingEmailFieldUnusable);
    }

    /// <summary>A message addressed to nobody has nothing to compose.</summary>
    [Fact]
    public void Compose_MessageAddressedToNobody_IsRefused()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.Compose(Account, Requester(), Authored() with { Recipients = [] }, Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.FieldUnusable,
            AuthoredEmailField.Recipients,
            MailFathomErrorCode.OutgoingEmailFieldUnusable);
    }

    /// <summary>
    /// A draft addressed to nobody is composed all the same, which is the one difference between the two entry points:
    /// writing the message before deciding who reads it is what a draft is for, and nothing is transmitted from one.
    /// </summary>
    [Fact]
    public void ComposeDraft_MessageAddressedToNobody_ComposesItAnyway()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.ComposeDraft(Account, Authored() with { Recipients = [] }, Capabilities());

        // Assert
        Assert.True(composition.IsComposed);
        Assert.Empty(composition.Draft!.Recipients);
        Assert.Empty(ParseDraft(composition).To);
    }

    /// <summary>
    /// A mailbox named twice is offered once, and the mention that survives on the composed draft is the strictest one:
    /// a caller that also wrote down an address this system derived has named that address, and naming it is what the
    /// sending governance weighs. A promotion judging the derived reading would admit a recipient the same reply sent
    /// outright is refused for, because the direct send is judged before this dedup.
    /// </summary>
    [Fact]
    public void ComposeDraft_AddressBothDerivedAndNamedByTheCaller_RecordsItAsNamedByTheCaller()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients =
            [
                new AuthoredEmailRecipient(
                    OutgoingRecipientRole.To,
                    "anna@example.test",
                    Provenance: AuthoredRecipientProvenance.DerivedFromAnsweredEmail),
                new AuthoredEmailRecipient(
                    OutgoingRecipientRole.To,
                    "anna@example.test",
                    Provenance: AuthoredRecipientProvenance.NamedByCaller),
            ],
        };

        // Act
        var composition = composer.ComposeDraft(Account, authored, Capabilities());

        // Assert
        var recipient = Assert.Single(composition.Draft!.Recipients);
        Assert.Equal("anna@example.test", recipient.Recipient.Address.Address);
        Assert.Equal(AuthoredRecipientProvenance.NamedByCaller, recipient.Provenance);
        Assert.Single(ParseDraft(composition).To);
    }

    /// <summary>
    /// The narrowing runs one way only. An address the caller named that the answered message also named stays the
    /// caller's word, and one only this system derived is not turned into the caller's by a second derived mention.
    /// </summary>
    [Fact]
    public void ComposeDraft_AddressDerivedTwice_StaysDerivedFromTheAnsweredEmail()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients =
            [
                new AuthoredEmailRecipient(
                    OutgoingRecipientRole.To,
                    "anna@example.test",
                    Provenance: AuthoredRecipientProvenance.DerivedFromAnsweredEmail),
                new AuthoredEmailRecipient(
                    OutgoingRecipientRole.Cc,
                    "anna@example.test",
                    Provenance: AuthoredRecipientProvenance.DerivedFromAnsweredEmail),
            ],
        };

        // Act
        var composition = composer.ComposeDraft(Account, authored, Capabilities());

        // Assert
        var recipient = Assert.Single(composition.Draft!.Recipients);
        Assert.Equal(
            AuthoredRecipientProvenance.DerivedFromAnsweredEmail,
            recipient.Provenance);
        Assert.Equal(OutgoingRecipientRole.To, recipient.Recipient.Role);
    }

    /// <summary>A draft is otherwise the same message a send is, down to the headers this system owns.</summary>
    [Fact]
    public void ComposeDraft_AuthoredMessage_WritesTheSameHeadersASendWould()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.ComposeDraft(Account, Authored(), Capabilities());

        // Assert
        var message = ParseDraft(composition);
        var sender = Assert.IsType<MailboxAddress>(Assert.Single(message.From));
        Assert.Equal("mailfathom@example.test", sender.Address);
        Assert.Equal(composition.Draft!.MessageId.Value, message.MessageId);
        Assert.Equal(ComposedAt, message.Date);
    }

    /// <summary>Every bound a send is refused for refuses a draft too, because a draft is one command away from one.</summary>
    [Fact]
    public void ComposeDraft_BodyLongerThanTheDeploymentComposes_IsRefused()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.ComposeDraft(
            Account,
            Authored() with { PlainTextBody = new string('a', 65) },
            Capabilities());

        // Assert
        Assert.False(composition.IsComposed);
        Assert.Equal(AuthoredEmailRefusalReason.BoundExceeded, composition.Refusal!.Reason);
        Assert.Equal(AuthoredEmailField.PlainTextBody, composition.Refusal.Field);
    }

    /// <summary>A subject is a header, so a line break in one is refused rather than stripped.</summary>
    [Fact]
    public void Compose_SubjectCarryingALineBreak_IsRefused()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.Compose(
            Account,
            Requester(),
            Authored() with { Subject = Injection },
            Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.HeaderInjected,
            AuthoredEmailField.Subject,
            MailFathomErrorCode.OutgoingEmailHeaderInjected);
    }

    /// <summary>The name written beside an address is a header too, and is refused against the header it would sit in.</summary>
    [Fact]
    public void Compose_RecipientDisplayNameCarryingALineBreak_IsRefusedNamingTheHeader()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients = [new AuthoredEmailRecipient(OutgoingRecipientRole.Cc, "anna@example.test", Injection)],
        };

        // Act
        var composition = composer.Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.HeaderInjected,
            AuthoredEmailField.Cc,
            MailFathomErrorCode.OutgoingEmailHeaderInjected);
    }

    /// <summary>A file's name and its declared media type both become headers of the part that carries it.</summary>
    [Fact]
    public void Compose_AttachmentNameCarryingALineBreak_IsRefused()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Attachments = [new AuthoredEmailAttachment(Injection, "text/plain", new byte[] { 1, 2, 3 })],
        };

        // Act
        var composition = composer.Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.HeaderInjected,
            AuthoredEmailField.Attachment,
            MailFathomErrorCode.OutgoingEmailHeaderInjected);
    }

    /// <summary>The declared media type becomes a header of that same part, so it is read for a break exactly as the name is.</summary>
    [Fact]
    public void Compose_AttachmentMediaTypeCarryingALineBreak_IsRefused()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Attachments = [new AuthoredEmailAttachment("report.csv", $"text/plain{Injection}", new byte[] { 1, 2, 3 })],
        };

        // Act
        var composition = composer.Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.HeaderInjected,
            AuthoredEmailField.Attachment,
            MailFathomErrorCode.OutgoingEmailHeaderInjected);
    }

    /// <summary>An address the submission server cannot carry is refused before anything is transmitted.</summary>
    [Fact]
    public void Compose_InternationalizedAddressAgainstServerWithoutSupport_IsRefused()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients = [new AuthoredEmailRecipient(OutgoingRecipientRole.To, "zoë@example.test")],
        };

        // Act
        var composition = composer.Compose(
            Account,
            Requester(),
            authored,
            Capabilities(acceptsInternationalizedAddresses: false));

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.InternationalizationUnsupported,
            AuthoredEmailField.To,
            MailFathomErrorCode.OutgoingEmailInternationalizationUnsupported);
    }

    /// <summary>The same address is composed when the server advertised that it carries one.</summary>
    [Fact]
    public void Compose_InternationalizedAddressAgainstServerAdvertisingSupport_IsComposed()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients = [new AuthoredEmailRecipient(OutgoingRecipientRole.To, "zoë@example.test")],
        };

        // Act
        var composition = composer.Compose(
            Account,
            Requester(),
            authored,
            Capabilities(acceptsInternationalizedAddresses: true));

        // Assert
        Assert.True(composition.IsComposed);
        Assert.Equal(
            "zoë@example.test",
            Assert.IsType<MailboxAddress>(Assert.Single(Parse(composition).To)).Address);
    }

    /// <summary>
    /// An address is written as raw UTF-8 or not at all, so a server advertising internationalized addresses without
    /// eight-bit content has not said it will accept one — and RFC 6531 requires it to advertise both.
    /// </summary>
    [Fact]
    public void Compose_InternationalizedAddressAgainstServerWithoutEightBitContent_IsRefused()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with
        {
            Recipients = [new AuthoredEmailRecipient(OutgoingRecipientRole.To, "zoë@example.test")],
        };

        // Act
        var composition = composer.Compose(
            Account,
            Requester(),
            authored,
            Capabilities(acceptsEightBitContent: false, acceptsInternationalizedAddresses: true));

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.InternationalizationUnsupported,
            AuthoredEmailField.To,
            MailFathomErrorCode.OutgoingEmailInternationalizationUnsupported);
    }

    /// <summary>
    /// The account's own address is checked beside the recipients, so a deployment configured with an internationalized
    /// mailbox that reaches a relay carrying none reads the refusal as its configuration rather than as a mistake an
    /// author made.
    /// </summary>
    [Fact]
    public void Compose_SendingAddressOutsideAsciiAgainstServerWithoutSupport_IsRefusedNamingTheSender()
    {
        // Arrange
        var composer = CreateComposer(Bounds(), "zoë@example.test");

        // Act
        var composition = composer.Compose(
            Account,
            Requester(),
            Authored(),
            Capabilities(acceptsInternationalizedAddresses: false));

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.InternationalizationUnsupported,
            AuthoredEmailField.Sender,
            MailFathomErrorCode.OutgoingEmailInternationalizationUnsupported);
    }

    /// <summary>A subject outside ASCII needs no such support: it is encoded the way every mail transport has always carried one.</summary>
    [Fact]
    public void Compose_SubjectOutsideAsciiAgainstServerWithoutInternationalization_IsComposedAndReadsBack()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.Compose(
            Account,
            Requester(),
            Authored() with { Subject = "Zażółć gęślą" },
            Capabilities(acceptsInternationalizedAddresses: false));

        // Assert
        Assert.True(composition.IsComposed);
        Assert.Equal("Zażółć gęślą", Parse(composition).Subject);
    }

    /// <summary>A message with one body is that body, and the author's own text is what it carries.</summary>
    [Fact]
    public void Compose_PlainTextOnly_ProducesAPlainTextMessage()
    {
        // Act
        var composition = CreateComposer().Compose(Account, Requester(), Authored(), Capabilities());

        // Assert
        var message = Parse(composition);
        Assert.Null(message.HtmlBody);
        Assert.Equal("The report is attached.", message.TextBody?.Trim());
    }

    /// <summary>Where both exist the message is a proper alternative, and the plain text is the author's rather than a reading of the markup.</summary>
    [Fact]
    public void Compose_BothBodies_ProducesAnAlternativeCarryingTheAuthorsOwnPlainText()
    {
        // Arrange
        var authored = Authored() with { HtmlBody = "<p>The <b>report</b> is attached.</p>" };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        var message = Parse(composition);
        Assert.IsType<MultipartAlternative>(message.Body);
        Assert.Equal("The report is attached.", message.TextBody?.Trim());
        Assert.Equal("<p>The <b>report</b> is attached.</p>", message.HtmlBody);
    }

    /// <summary>An attached file is carried under the name and the media type the author declared it as.</summary>
    [Fact]
    public void Compose_Attachment_CarriesItsNameAndDeclaredMediaType()
    {
        // Arrange
        var authored = Authored() with
        {
            Attachments = [new AuthoredEmailAttachment("report.csv", "text/csv", "id,total\n1,2\n"u8.ToArray())],
        };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        var attachment = Assert.IsAssignableFrom<MimePart>(Assert.Single(Parse(composition).Attachments));
        Assert.Equal("report.csv", attachment.FileName);
        Assert.Equal("text/csv", attachment.ContentType.MimeType);
    }

    /// <summary>A file declared as something that is not a media type composes nothing.</summary>
    [Fact]
    public void Compose_AttachmentDeclaredAsNoMediaType_IsRefused()
    {
        // Arrange
        var authored = Authored() with
        {
            Attachments = [new AuthoredEmailAttachment("report.csv", "not a media type", new byte[] { 1 })],
        };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.FieldUnusable,
            AuthoredEmailField.Attachment,
            MailFathomErrorCode.OutgoingEmailFieldUnusable);
    }

    /// <summary>More people than the deployment addresses one message to is refused, and the refusal names that number.</summary>
    [Fact]
    public void Compose_MoreRecipientsThanConfigured_IsRefusedNamingTheBound()
    {
        // Arrange
        var authored = Authored() with
        {
            Recipients =
            [
                .. Enumerable.Range(0, 4).Select(index => new AuthoredEmailRecipient(
                    OutgoingRecipientRole.To,
                    $"anna{index}@example.test")),
            ],
        };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertBoundExceeded(composition, AuthoredEmailField.Recipients, Bounds().MaxRecipientCount);
    }

    /// <summary>
    /// Resolution can only drop repeated mentions, and one mailbox is worth naming in three headers at most — so a
    /// list longer than three times the bound is refused on its authored length, before any of it is parsed.
    /// </summary>
    [Fact]
    public void Compose_MoreMentionsThanRedundancyCouldExplain_IsRefusedOnTheAuthoredLength()
    {
        // Arrange
        var mentions = (Bounds().MaxRecipientCount * 3) + 1;
        var authored = Authored() with
        {
            Recipients =
            [
                .. Enumerable.Range(0, mentions).Select(_ => new AuthoredEmailRecipient(
                    OutgoingRecipientRole.To,
                    "anna@example.test")),
            ],
        };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertBoundExceeded(composition, AuthoredEmailField.Recipients, Bounds().MaxRecipientCount);
    }

    /// <summary>
    /// An author who wrote only markup has written no message this system composes, so the markup is refused rather
    /// than sent beside a plain text that would show every client preferring it nothing at all.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t")]
    public void Compose_MarkupBesideAPlainTextNobodyWrote_IsRefused(string plainTextBody)
    {
        // Arrange
        var authored = Authored() with
        {
            PlainTextBody = plainTextBody,
            HtmlBody = "<p>The quarterly report is attached.</p>",
        };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.FieldUnusable,
            AuthoredEmailField.PlainTextBody,
            MailFathomErrorCode.OutgoingEmailFieldUnusable);
    }

    /// <summary>An alternative offered blank is the same failure read from the other side, and is refused as one.</summary>
    [Fact]
    public void Compose_AlternativeThatWouldBeOfferedBlank_IsRefused()
    {
        // Arrange
        var authored = Authored() with { HtmlBody = "   " };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.FieldUnusable,
            AuthoredEmailField.HtmlBody,
            MailFathomErrorCode.OutgoingEmailFieldUnusable);
    }

    /// <summary>Each body is bounded on its own, so a long one of either kind is refused against its own field.</summary>
    [Fact]
    public void Compose_PlainTextBodyBeyondTheConfiguredLength_IsRefusedNamingIt()
    {
        // Arrange
        var authored = Authored() with { PlainTextBody = new string('a', Bounds().MaxBodyCharacters + 1) };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertBoundExceeded(composition, AuthoredEmailField.PlainTextBody, Bounds().MaxBodyCharacters);
    }

    /// <summary>The HTML alternative is measured separately, so a long one cannot hide behind a short plain text.</summary>
    [Fact]
    public void Compose_HtmlBodyBeyondTheConfiguredLength_IsRefusedNamingIt()
    {
        // Arrange
        var authored = Authored() with { HtmlBody = new string('a', Bounds().MaxBodyCharacters + 1) };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertBoundExceeded(composition, AuthoredEmailField.HtmlBody, Bounds().MaxBodyCharacters);
    }

    /// <summary>More files than the deployment attaches to one message is refused.</summary>
    [Fact]
    public void Compose_MoreAttachmentsThanConfigured_IsRefusedNamingTheBound()
    {
        // Arrange
        var authored = Authored() with
        {
            Attachments =
            [
                .. Enumerable.Range(0, Bounds().MaxAttachmentCount + 1).Select(index => new AuthoredEmailAttachment(
                    $"file{index}.bin",
                    "application/octet-stream",
                    new byte[] { 1 })),
            ],
        };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertBoundExceeded(composition, AuthoredEmailField.Attachment, Bounds().MaxAttachmentCount);
    }

    /// <summary>A file larger than the deployment attaches is refused before the message is assembled around it.</summary>
    [Fact]
    public void Compose_AttachmentLargerThanConfigured_IsRefusedNamingTheBound()
    {
        // Arrange
        var authored = Authored() with
        {
            Attachments =
            [
                new AuthoredEmailAttachment(
                    "file.bin",
                    "application/octet-stream",
                    new byte[Bounds().MaxAttachmentBytes + 1]),
            ],
        };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertBoundExceeded(composition, AuthoredEmailField.Attachment, Bounds().MaxAttachmentBytes);
    }

    /// <summary>
    /// Files that are each small enough and few enough still carry more octets together than the message may be
    /// transmitted as, and that is answered on the octets the author supplied rather than after they are expanded.
    /// </summary>
    [Fact]
    public void Compose_AttachmentsTogetherLargerThanTheMessageBound_IsRefusedNamingTheMessageBound()
    {
        // Arrange
        var bounds = Bounds() with { MaxMessageBytes = 200 };
        var authored = Authored() with
        {
            Attachments =
            [
                .. Enumerable.Range(0, bounds.MaxAttachmentCount).Select(index => new AuthoredEmailAttachment(
                    $"file-{index}.bin",
                    "application/octet-stream",
                    new byte[bounds.MaxAttachmentBytes])),
            ],
        };

        // Act
        var composition = CreateComposer(bounds).Compose(Account, Requester(), authored, Capabilities());

        // Assert
        AssertBoundExceeded(composition, AuthoredEmailField.Message, bounds.MaxMessageBytes);
    }

    /// <summary>
    /// The whole message is measured on what it became rather than on what the author supplied, because transfer
    /// encoding, headers, and boundaries are the difference between the two.
    /// </summary>
    [Fact]
    public void Compose_ComposedMessageLargerThanConfigured_IsRefusedNamingTheBound()
    {
        // Arrange
        var bounds = Bounds() with { MaxMessageBytes = 64 };
        var composer = CreateComposer(bounds);

        // Act
        var composition = composer.Compose(Account, Requester(), Authored(), Capabilities());

        // Assert
        AssertBoundExceeded(composition, AuthoredEmailField.Message, 64);
    }

    /// <summary>A server that declared a smaller message than the deployment permits is the one that decides.</summary>
    [Fact]
    public void Compose_MessageLargerThanTheServerAdvertised_IsRefusedNamingTheServersBound()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var composition = composer.Compose(Account, Requester(), Authored(), Capabilities(maxMessageBytes: 64));

        // Assert
        AssertBoundExceeded(composition, AuthoredEmailField.Message, 64);
    }

    /// <summary>A server that takes no eight-bit content is handed a message whose octets are all seven-bit.</summary>
    [Fact]
    public void Compose_ServerWithoutEightBitContent_TransferEncodesTheBodyToSevenBits()
    {
        // Arrange
        var composer = CreateComposer();
        var authored = Authored() with { PlainTextBody = "Zażółć gęślą" };

        // Act
        var composition = composer.Compose(
            Account,
            Requester(),
            authored,
            Capabilities(acceptsEightBitContent: false));

        // Assert
        Assert.True(composition.IsComposed);
        Assert.DoesNotContain(composition.Email!.RawMime.ToArray(), static octet => octet > 127);
        Assert.Equal("Zażółć gęślą", Parse(composition).TextBody?.Trim());
    }

    /// <summary>The stored bytes are transmitted verbatim, so they carry the line ending SMTP requires.</summary>
    [Fact]
    public void Compose_ComposedMessage_IsWrittenWithNetworkLineEndings()
    {
        // Act
        var composition = CreateComposer().Compose(Account, Requester(), Authored(), Capabilities());

        // Assert
        var text = Encoding.UTF8.GetString(composition.Email!.RawMime.Span);
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', text.Replace("\r\n", string.Empty, StringComparison.Ordinal));
    }

    /// <summary>The record and the message describe one send, so the request names the account and the act that asked.</summary>
    [Fact]
    public void Compose_AuthoredMessage_RecordsTheRequestAgainstTheAskingAccountAndRequester()
    {
        // Arrange
        var requester = Requester();

        // Act
        var composition = CreateComposer().Compose(Account, requester, Authored(), Capabilities());

        // Assert
        Assert.Equal(Account, composition.Email!.Request.AccountId);
        Assert.Equal(requester, composition.Email.Request.Requester);
    }

    /// <summary>
    /// A message answering another carries the two headers every client threads by, written by the one place that
    /// assembles a message. The answered identifier is last in the path, which is where a client looks for the parent.
    /// </summary>
    [Fact]
    public void Compose_MessageAnsweringAnother_WritesBothHeadersEveryClientThreadsBy()
    {
        // Arrange
        var answered = EmailThreadReferences.Create(
            "<parent@example.test>",
            inReplyTo: "<root@example.test>",
            references: ["<root@example.test>"]);
        var authored = Authored() with { Threading = OutgoingThreadPlacement.Answering(answered) };

        // Act
        var composition = CreateComposer().Compose(Account, Requester(), authored, Capabilities());

        // Assert
        var message = Parse(composition);
        Assert.Equal("parent@example.test", message.InReplyTo);
        Assert.Equal(["root@example.test", "parent@example.test"], message.References.ToArray());
    }

    /// <summary>A message answering nothing writes neither header, which is what a parser reads as a first message.</summary>
    [Fact]
    public void Compose_MessageAnsweringNothing_WritesNeitherThreadingHeader()
    {
        // Act
        var composition = CreateComposer().Compose(Account, Requester(), Authored(), Capabilities());

        // Assert
        var message = Parse(composition);
        Assert.Null(message.InReplyTo);
        Assert.Empty(message.References);
    }

    private static void AssertBoundExceeded(
        AuthoredEmailComposition composition,
        AuthoredEmailField field,
        long bound)
    {
        AssertRefused(
            composition,
            AuthoredEmailRefusalReason.BoundExceeded,
            field,
            MailFathomErrorCode.OutgoingEmailBoundExceeded);
        Assert.Equal(bound, composition.Refusal!.Bound);
    }

    private static void AssertRefused(
        AuthoredEmailComposition composition,
        AuthoredEmailRefusalReason reason,
        AuthoredEmailField field,
        MailFathomErrorCode failure)
    {
        Assert.False(composition.IsComposed);
        Assert.Null(composition.Email);
        Assert.Equal(reason, composition.Refusal!.Reason);
        Assert.Equal(field, composition.Refusal.Field);
        Assert.Equal(failure, composition.Refusal.Failure);
    }

    private static MimeMessage Parse(AuthoredEmailComposition composition)
    {
        Assert.True(composition.IsComposed);

        using var stream = new MemoryStream(composition.Email!.RawMime.ToArray());

        return MimeMessage.Load(stream);
    }

    private static MimeMessage ParseDraft(MailDraftComposition composition)
    {
        Assert.True(composition.IsComposed);

        using var stream = new MemoryStream(composition.Draft!.RawMime.ToArray());

        return MimeMessage.Load(stream);
    }

    private static MimeKitAuthoredEmailComposer CreateComposer() => CreateComposer(Bounds());

    private static MimeKitAuthoredEmailComposer CreateComposer(
        OutgoingEmailBounds bounds,
        string sendingAddress = "mailfathom@example.test")
    {
        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        Assert.True(EmailAddress.TryCreate("MailFathom", sendingAddress, out var address));
        senderIdentities.FindSenderIdentity(Account).Returns(OutgoingSenderIdentity.Create(Account, address));

        return new MimeKitAuthoredEmailComposer(senderIdentities, bounds, new FakeTimeProvider(ComposedAt));
    }

    private static OutgoingEmailBounds Bounds() => new()
    {
        MaxRecipientCount = 3,
        MaxBodyCharacters = 64,
        MaxAttachmentCount = 2,
        MaxAttachmentBytes = 128,
        MaxMessageBytes = 1024,
    };

    private static MailDeliveryCapabilities Capabilities(
        long? maxMessageBytes = null,
        bool acceptsEightBitContent = true,
        bool acceptsInternationalizedAddresses = true) =>
        new(maxMessageBytes, acceptsEightBitContent, acceptsInternationalizedAddresses);

    private static OutgoingEmailRequester Requester() => OutgoingEmailRequester.Command("send-quarterly-report");

    private static AuthoredEmail Authored() => new()
    {
        Recipients = [new AuthoredEmailRecipient(OutgoingRecipientRole.To, "anna@example.test", "Anna")],
        Subject = "Quarterly report",
        PlainTextBody = "The report is attached.",
    };
}
