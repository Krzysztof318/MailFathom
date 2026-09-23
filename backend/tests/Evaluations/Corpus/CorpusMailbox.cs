// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Evaluations.Corpus;

/// <summary>The suite's corpora as one mailbox, held the way the store holds it, for the store's own queries to run over.</summary>
/// <remarks>
/// <para>
/// A scenario whose input a deployment selects in SQL runs the store's query itself over these rows, so the selection —
/// its window, its caps, its ordering — is the one a deployment runs rather than a restatement of it. Only what those
/// queries read is written: identity, subject, instants, the conversation, the normalized sender and recipients, and one
/// attachment-text row for each attachment the production walk finds, up to a deployment's default count, which is what
/// a deployment reading attachment text writes whatever each attachment's reading turned out to be.
/// </para>
/// <para>
/// The corpus comes first, then the written conversations, the hostile ones, and the Polish ones, so every conversation
/// before the Polish corpus keeps the thread identifier its position derives.
/// </para>
/// </remarks>
internal static class CorpusMailbox
{
    /// <summary>The address the mailbox belongs to and sends from.</summary>
    public const string OwnerAddress = "owner@example.test";

    private static readonly Lazy<IReadOnlyList<StoredEmailEntity>> Rows = new(static () =>
    [
        .. Exchanges.SelectMany(static (exchange, index) => exchange.Select(message => StoredOf(
            message,
            new Guid(index + 1, 1, 0, new byte[8])))),
    ]);

    /// <summary>Gets every conversation the mailbox holds.</summary>
    public static IEnumerable<IReadOnlyList<CorpusMessage>> Exchanges =>
        CorpusMessage.Exchanges.Concat(WrittenCorpus.Exchanges).Concat(HostileMail.Exchanges).Concat(PolishCorpus.Exchanges);

    /// <summary>Gets every message as the store holds it.</summary>
    public static IReadOnlyList<StoredEmailEntity> Stored => Rows.Value;

    /// <summary>Gets the instant of the newest message the mailbox holds.</summary>
    public static DateTimeOffset Newest => Stored.Max(static email => email.ReceivedAt!.Value);

    /// <summary>Finds the row one message is stored as.</summary>
    /// <param name="message">A message of the mailbox.</param>
    /// <returns>Its row.</returns>
    public static StoredEmailEntity StoredAs(CorpusMessage message) =>
        Stored.Single(email => email.Id == message.Id.Value);

    /// <summary>Finds the message one row stores.</summary>
    /// <param name="stored">A row of <see cref="Stored" />.</param>
    /// <returns>The message.</returns>
    public static CorpusMessage MessageStoredAs(StoredEmailEntity stored) =>
        Exchanges.SelectMany(static exchange => exchange).Single(message => message.Id.Value == stored.Id);

    /// <summary>Normalizes an address the way the store writes one.</summary>
    /// <param name="address">The address as a message carried it.</param>
    /// <returns>The normalized address, or <see langword="null" /> where it is not one.</returns>
    public static string? NormalizedAddressOf(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed) ? parsed.NormalizedAddress : null;

    private static StoredEmailEntity StoredOf(CorpusMessage message, Guid threadId)
    {
        // The folder is required of every row and read by none of the queries run over these.
        var stored = new StoredEmailEntity
        {
            Id = message.Id.Value,
            MailboxAccountId = string.Empty,
            MailFolder = null!,
            Subject = message.Subject,
            SentAt = message.ReceivedAt,
            ReceivedAt = message.ReceivedAt,
            EmailThreadId = threadId,
            SenderDisplayName = message.SenderName,
            SenderAddress = message.Sender,
            SenderNormalizedAddress = NormalizedAddressOf(message.Sender),
            ToAddresses = [.. message.Recipients.Select(NormalizedAddressOf).OfType<string>()],
        };

        var texts = message.Attachments
            .Take(new AttachmentTextOptions().MaxAttachmentsPerEmail)
            .Select((attachment, position) => new EmailAttachmentTextEntity
            {
                StoredEmailId = stored.Id,
                StoredEmail = stored,
                AttachmentPosition = position,
                DeclaredMediaType = attachment.DeclaredMediaType,
                FileName = attachment.FileName,
                Outcome = string.Empty,
            });

        foreach (var text in texts)
        {
            stored.AttachmentTexts.Add(text);
        }

        return stored;
    }
}
