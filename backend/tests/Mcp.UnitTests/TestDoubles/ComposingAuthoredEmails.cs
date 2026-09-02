// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using NSubstitute;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Builds compositions that pass whatever they are handed through, so a suite here says nothing about MIME.</summary>
/// <remarks>
/// What a composition test needs from this seam is the recipients the resolution produced, because that is what the
/// outbox writes down and what a repeat has to reach the same row for. The headers, the encoding, and the bytes belong
/// to the MimeKit adapter's own suite, so the message a caller asks for comes back as the literal the test named.
/// </remarks>
internal static class ComposingAuthoredEmails
{
    /// <summary>Builds a composer that composes a send, carrying the recipients it was handed.</summary>
    /// <param name="mime">The message every composition answers with.</param>
    /// <returns>The composer.</returns>
    internal static IAuthoredEmailComposer ThatComposes(ReadOnlyMemory<byte> mime)
    {
        var composer = Substitute.For<IAuthoredEmailComposer>();
        composer
            .Compose(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<OutgoingEmailRequester>(),
                Arg.Any<AuthoredEmail>(),
                Arg.Any<MailDeliveryCapabilities>())
            .Returns(call => AuthoredEmailComposition.Composed(new ComposedOutgoingEmail(
                OutgoingEmailRequest.Create(
                    call.ArgAt<MailAccountIdentity>(0),
                    call.ArgAt<OutgoingEmailRequester>(1),
                    RecipientsOf(call.ArgAt<AuthoredEmail>(2))),
                InternetMessageId.Mint("example.test"),
                mime)));

        return composer;
    }

    /// <summary>Builds a composer that composes a draft, carrying the recipients it was handed.</summary>
    /// <param name="mime">The message every composition answers with.</param>
    /// <returns>The composer.</returns>
    internal static IAuthoredEmailComposer ThatComposesDrafts(ReadOnlyMemory<byte> mime)
    {
        var composer = Substitute.For<IAuthoredEmailComposer>();
        composer
            .ComposeDraft(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<AuthoredEmail>(),
                Arg.Any<MailDeliveryCapabilities>())
            .Returns(call => MailDraftComposition.Composed(new ComposedMailDraft(
                DraftRecipientsOf(call.ArgAt<AuthoredEmail>(1)),
                InternetMessageId.Mint("example.test"),
                mime)));

        return composer;
    }

    /// <summary>Reads the recipients a draft's resolution produced, each keeping the provenance the record stores.</summary>
    private static IReadOnlyList<MailDraftRecipient> DraftRecipientsOf(AuthoredEmail authored) =>
    [
        .. authored.Recipients.Select(recipient => new MailDraftRecipient(
            OutgoingRecipient.Create(Address(recipient.Address), recipient.Role, recipient.Contact),
            recipient.Provenance)),
    ];

    /// <summary>Reads the recipients a resolution produced, failing the suite rather than the composition on a bad one.</summary>
    private static IReadOnlyList<OutgoingRecipient> RecipientsOf(AuthoredEmail authored) =>
    [
        .. authored.Recipients.Select(recipient => OutgoingRecipient.Create(
            Address(recipient.Address),
            recipient.Role,
            recipient.Contact)),
    ];

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress)
            ? emailAddress
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
}
