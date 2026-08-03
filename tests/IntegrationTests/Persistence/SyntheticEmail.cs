// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.AppHost;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.IntegrationTests.Orchestration;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Builds the occurrence identities, metadata, and raw MIME the persistence tests write.</summary>
/// <remarks>
/// Every value is stated rather than read from the clock, so a row read back is comparable against the same literal the
/// write used and a failure names a difference rather than a drifting timestamp. Nothing here comes from a real mailbox:
/// the addresses are in the reserved <c>.test</c> domain and the bodies are generated text.
/// </remarks>
internal static class SyntheticEmail
{
    /// <summary>The UIDVALIDITY every occurrence written without the mail server is scoped by.</summary>
    /// <remarks>
    /// Above <see cref="int.MaxValue" /> deliberately. UIDVALIDITY is an IMAP 32-bit unsigned value, so a mapping onto a
    /// signed 32-bit column would round-trip most values and silently fail on these; the baseline migration maps it to
    /// <c>bigint</c> and this constant is what makes a run notice if that ever changes.
    /// </remarks>
    internal const uint UidValidity = 4_000_000_001;

    /// <summary>The instant the fixed metadata reports as the message's send time.</summary>
    internal static readonly DateTimeOffset SentAt = new(2026, 5, 4, 8, 30, 0, TimeSpan.Zero);

    /// <summary>The instant the fixed extraction reports as the last receiving hop's.</summary>
    internal static readonly DateTimeOffset ReceivedAt = new(2026, 5, 4, 8, 31, 0, TimeSpan.Zero);

    /// <summary>Names one occurrence under a committed folder binding.</summary>
    /// <param name="binding">The binding the occurrence is read under.</param>
    /// <param name="uid">The occurrence's UID within that binding's UIDVALIDITY scope.</param>
    /// <returns>The occurrence identity.</returns>
    internal static EmailOccurrenceId OccurrenceIn(MailFolderResolution binding, uint uid) =>
        EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            binding.Id,
            ImapUidValidity.Create(UidValidity),
            ImapUid.Create(uid));

    /// <summary>Builds the summary a mail server's envelope would have reported for one occurrence.</summary>
    /// <param name="occurrenceId">The occurrence the summary describes.</param>
    /// <param name="subject">The subject, which is how a test recognizes its own row.</param>
    /// <param name="sizeOctets">The size the server reported.</param>
    /// <returns>The remote summary.</returns>
    internal static RemoteEmailMetadata RemoteMetadataOf(
        EmailOccurrenceId occurrenceId,
        string subject,
        long sizeOctets = 2048) =>
        new(occurrenceId, $"{subject}@mailfathom.test", subject, SentAt, sizeOctets);

    /// <summary>Builds the metadata a MIME reader would have extracted from one message's stored bytes.</summary>
    /// <param name="occurrenceId">The occurrence the metadata was read from.</param>
    /// <param name="subject">The decoded subject.</param>
    /// <param name="bodyText">The searchable text the body yielded.</param>
    /// <param name="recipientAddresses">The addresses the <c>To</c> header carried, beside the fixed sender.</param>
    /// <returns>The extracted metadata.</returns>
    internal static ExtractedEmailMetadata ExtractionOf(
        EmailOccurrenceId occurrenceId,
        string subject,
        string bodyText,
        params string[] recipientAddresses) =>
        new(
            occurrenceId,
            subject,
            SentAt,
            ReceivedAt,
            [
                Participant(EmailAddressRole.From, "sender@mailfathom.test"),
                .. recipientAddresses.Select(address => Participant(EmailAddressRole.To, address)),
            ],
            EmailThreadReferences.Create($"{subject}@mailfathom.test", inReplyTo: null, references: null),
            EmailAttachmentSummary.None,
            ExtractedEmailText.FromPlainTextBody(bodyText, bodyText));

    /// <summary>Builds raw RFC 822 bytes of a requested length, padded with text that does not compress.</summary>
    /// <param name="subject">The subject the headers carry.</param>
    /// <param name="totalByteCount">The length the produced payload must have.</param>
    /// <returns>The raw MIME bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="totalByteCount" /> cannot hold the headers.</exception>
    /// <remarks>
    /// The padding has to be incompressible, because PostgreSQL compresses an oversized value before it considers
    /// storing it out of line: repeated text of any length would compress back under the threshold and the out-of-line
    /// path a large payload exists to exercise would never be taken. It is produced by hashing a counter rather than by a
    /// random generator, so the same subject and length always yield the same bytes and a failing run can be repeated.
    /// </remarks>
    internal static byte[] RawMimeOf(string subject, int totalByteCount)
    {
        var headers = Encoding.ASCII.GetBytes(
            $"From: sender@mailfathom.test\r\nTo: {OrchestrationContract.MailServerAccountEmailAddress}\r\nSubject: {subject}\r\n\r\n");

        ArgumentOutOfRangeException.ThrowIfLessThan(totalByteCount, headers.Length);

        var rawMime = new byte[totalByteCount];
        headers.CopyTo(rawMime, 0);
        FillWithIncompressiblePadding(rawMime.AsSpan(headers.Length));

        return rawMime;
    }

    /// <summary>Builds body text of roughly a requested length out of distinct words.</summary>
    /// <param name="term">The word that appears once and makes the text findable by a search for it alone.</param>
    /// <param name="wordCount">How many filler words follow it.</param>
    /// <returns>The body text.</returns>
    /// <remarks>
    /// The filler words are numbered rather than repeated, so a lexical index over the text holds as many distinct
    /// lexemes as a real body would and a term's selectivity is not an artefact of one word appearing everywhere.
    /// </remarks>
    internal static string BodyTextContaining(string term, int wordCount) =>
        string.Join(' ', Enumerable.Range(0, wordCount).Select(index => $"filler{index}").Prepend(term));

    /// <summary>Writes bytes no compressor can shrink, from a sequence that is the same on every run.</summary>
    private static void FillWithIncompressiblePadding(Span<byte> padding)
    {
        Span<byte> block = stackalloc byte[SHA256.HashSizeInBytes];

        for (var counter = 0; padding.Length > 0; counter++)
        {
            SHA256.HashData(BitConverter.GetBytes(counter), block);

            var written = Math.Min(block.Length, padding.Length);
            block[..written].CopyTo(padding);
            padding = padding[written..];
        }
    }

    private static EmailParticipant Participant(EmailAddressRole role, string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new ArgumentException($"'{address}' is not an address the domain accepts.", nameof(address));
        }

        return new EmailParticipant(role, emailAddress);
    }
}
