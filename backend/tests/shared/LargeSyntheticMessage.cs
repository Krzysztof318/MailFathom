// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.TestSupport;

/// <summary>The one message every budget in this suite is measured against, and the text every chunking budget cuts.</summary>
/// <remarks>
/// <para>
/// It comes from the repository's own deterministic corpus rather than from a literal written here, so the shape being
/// measured is the shape a development mailbox is actually seeded with: a multipart message with headers, both body
/// alternatives, and one large attachment. A seed and a plan are the whole of what decides it, so two runs measure the
/// same bytes and a budget means the same thing on every machine.
/// </para>
/// <para>
/// The attachment ceiling is what makes the measurement discriminating. Every streaming path has a fixed cost — parse
/// buffers, the object tree, the bounded extracted text — and that cost says nothing about whether the payload was
/// streamed or copied. A message of several megabytes leaves the fixed part far below the budget and puts a whole
/// second copy of the payload far above it, which is the difference these budgets exist to catch.
/// </para>
/// <para>
/// Built once per assembly that compiles it. It is a few megabytes and several measurements read it, so composing it
/// per measurement would multiply the cost for nothing.
/// </para>
/// <para>
/// It is shared source rather than a fixture beside one measurement because both forms of cost claim read it: the
/// allocation budgets the unit suite gates on, and the nightly throughput report. A budget is a share of this message's
/// length, so the two would stop being comparable the moment each composed a message of its own.
/// </para>
/// </remarks>
internal static class LargeSyntheticMessage
{
    /// <summary>What the corpus is derived from, and therefore what every budget in this suite is measured against.</summary>
    private const int CorpusSeed = 20260821;

    /// <summary>How many messages are generated to draw the largest one from.</summary>
    /// <remarks>
    /// Enough that the generator's attachment frequency reliably produces several attachment-carrying messages at this
    /// seed, and small enough that generating them costs nothing worth measuring.
    /// </remarks>
    private const int CorpusSize = 40;

    /// <summary>The ceiling on one generated attachment, which is what makes the measured message large.</summary>
    private const int MaximumAttachmentBytes = 4 * 1024 * 1024;

    /// <summary>The least raw size the measured message may have for the budgets over it to mean anything.</summary>
    /// <remarks>
    /// Stated as an assertion rather than assumed, because every budget below is a share of this length: a generator
    /// change that quietly produced a small message would leave each of them satisfied by a path that buffered
    /// everything, and nothing else in this suite would notice.
    /// </remarks>
    private const int MinimumRawMimeBytes = 2 * 1024 * 1024;

    private static readonly Lazy<SyntheticEmail> Largest = new(SelectLargest);
    private static readonly Lazy<ReadOnlyMemory<byte>> RawMimeBytes = new(ComposeRawMime);
    private static readonly Lazy<ExtractedEmailText> ChunkableText = new(ComposeChunkableText);

    /// <summary>Gets the occurrence identity the measured message is read under.</summary>
    internal static EmailOccurrenceId OccurrenceId { get; } = EmailOccurrenceId.Create(
        MailAccountId.Create("primary"),
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("inbox"), RemoteFolderPath.Create("INBOX", '/')).Id,
        ImapUidValidity.Create(5),
        ImapUid.Create(10));

    /// <summary>Gets the raw RFC 822 bytes of the measured message.</summary>
    internal static ReadOnlyMemory<byte> RawMime => RawMimeBytes.Value;

    /// <summary>Gets the measured message as content a synchronization run has just fetched.</summary>
    internal static RemoteEmailContent AsFetched() => new(OccurrenceId, RawMime);

    /// <summary>Gets the measured message as content a store has just read back, recorded as intact.</summary>
    /// <remarks>
    /// The recorded length and digest are computed here rather than stated, so the integrity check a read performs is
    /// exercised as the passing case it is on ordinary mail. What a damaged copy costs is not a budget question.
    /// </remarks>
    internal static StoredEmailContent AsStored() =>
        new(RawMime, RawMime.Length, SHA256.HashData(RawMime.Span));

    /// <summary>Gets the extracted text a chunking budget is measured over.</summary>
    internal static ExtractedEmailText AsExtractedText() => ChunkableText.Value;

    /// <summary>Picks the message the corpus made largest, which is deterministic for the seed above.</summary>
    private static SyntheticEmail SelectLargest()
    {
        var corpus = SyntheticEmailGenerator.Generate(new SyntheticCorpusPlan(
            CorpusSeed,
            CorpusSize,
            new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero),
            SpanDays: 60,
            MaximumAttachmentBytes,
            SensitivePercentage: 0,
            Languages: [],
            Topics: []));

        return corpus
                   .Where(email => email.Attachment is not null)
                   .MaxBy(email => email.Attachment!.Length)
               ?? throw new InvalidOperationException(
                   "The corpus produced no message carrying an attachment, so there is no large message to measure against.");
    }

    private static ReadOnlyMemory<byte> ComposeRawMime()
    {
        var mailbox = new MailboxAddress("Mailbox", "mailbox@example.test");

        using var message = SyntheticMimeComposer.Compose(
            Largest.Value,
            mailbox,
            mailbox,
            SyntheticAuthorIdentity.Fabricated);

        using var raw = new MemoryStream();
        message.WriteTo(raw);

        var rawMime = raw.ToArray();

        return rawMime.Length >= MinimumRawMimeBytes
            ? rawMime
            : throw new InvalidOperationException(
                $"The measured message is {rawMime.Length} bytes, below the {MinimumRawMimeBytes} bytes every budget in this suite is a share of.");
    }

    /// <summary>Builds text long enough for a chunking budget out of the whole corpus rather than one message.</summary>
    /// <remarks>
    /// One generated body is a few paragraphs, which a chunker cuts into two or three passages — too little for the
    /// fixed cost of a cut to be distinguishable from what the passages themselves cost. The corpus's bodies joined by
    /// paragraph breaks are the same text a mailbox holds, at the length a chunking claim needs.
    /// </remarks>
    private static ExtractedEmailText ComposeChunkableText()
    {
        var corpus = SyntheticEmailGenerator.Generate(new SyntheticCorpusPlan(
            CorpusSeed,
            CorpusSize,
            new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero),
            SpanDays: 60,
            MaximumAttachmentBytes: 0,
            SensitivePercentage: 0,
            Languages: [],
            Topics: []));

        var text = string.Join("\n\n", corpus.Select(email => email.Body.PlainText));

        return ExtractedEmailText.FromPlainTextBody(text, text);
    }
}
