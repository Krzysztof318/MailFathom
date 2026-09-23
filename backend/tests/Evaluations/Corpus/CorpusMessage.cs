// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chunking;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.SyntheticMail.Corpus;
using MimeKit;

namespace MailFathom.Evaluations.Corpus;

/// <summary>One message of the committed synthetic corpus, cut into the passages a deployment would have stored for it.</summary>
/// <remarks>
/// <para>
/// The corpus is the only mail a scenario sends a provider. Every message in it was written for this repository — by a
/// model in <c>office-en.zip</c>, by hand in <c>hard-shapes-en.zip</c> — every address sits under a reserved domain, and
/// nobody received any of it, which is what lets a scenario, its cached answers, and its report be published.
/// <see cref="WrittenCorpus" /> adds the few kinds of message a structured agent's case needs and neither archive carries,
/// written by hand under the same rules and read through the same path, and keeps them out of <see cref="All" /> so no
/// scenario searching the corpus meets them. <see cref="PolishCorpus" /> reads the Polish archive through the same path,
/// and keeps it out of <see cref="All" /> for the same reason.
/// </para>
/// <para>
/// The passages come from the chunker a deployment runs, under the rules it runs with, so an agent is measured on the
/// shape of input it is actually given rather than on paragraphs cut here.
/// </para>
/// </remarks>
/// <param name="Id">The identifier a deployment would have stored the message under, which is what an answer cites it by.</param>
/// <param name="Subject">The subject.</param>
/// <param name="ReceivedAt">When the message is dated, which is what a relative date in it resolves against.</param>
/// <param name="Sender">The address it was sent from.</param>
/// <param name="SenderName">The name it was sent under, or <see langword="null" /> where it carried none.</param>
/// <param name="Recipients">The addresses in its To and Cc headers.</param>
/// <param name="Attachments">What it attached, in the order it carries them.</param>
/// <param name="Text">What it added, with the history it quoted trimmed off, which is what a conversation is derived from.</param>
/// <param name="Passages">The passages, in order.</param>
/// <param name="RawMime">The message as a deployment would have stored it, which is what its body is drawn from.</param>
internal sealed record CorpusMessage(
    StoredEmailId Id,
    string Subject,
    DateTimeOffset ReceivedAt,
    string Sender,
    string? SenderName,
    IReadOnlyList<string> Recipients,
    IReadOnlyList<CorpusAttachment> Attachments,
    string Text,
    IReadOnlyList<EnrichablePassage> Passages,
    ReadOnlyMemory<byte> RawMime)
{
    private static readonly Lazy<IReadOnlyList<IReadOnlyList<CorpusMessage>>> Delivered = new(static () =>
        ReadArchives(["office-en.zip", "hard-shapes-en.zip"], firstPosition: 0));

    private static readonly Lazy<IReadOnlyList<CorpusMessage>> Flattened =
        new(static () => [.. Delivered.Value.SelectMany(static exchange => exchange)]);

    /// <summary>Gets every message of the corpus, in delivery order.</summary>
    public static IReadOnlyList<CorpusMessage> All => Flattened.Value;

    /// <summary>Gets every conversation of the corpus, in delivery order, each in the order its messages were written.</summary>
    public static IReadOnlyList<IReadOnlyList<CorpusMessage>> Exchanges => Delivered.Value;

    /// <summary>Gets whether it carries an attachment.</summary>
    public bool HasAttachments => this.Attachments.Count > 0;

    /// <summary>Gets the message as the enrichment pass hands it to a derivation: the leading passages one derivation reads.</summary>
    /// <remarks>
    /// Every passage of a corpus message is cut from its body, so its passages are already in the order the pass reads
    /// them — body before attachment, then by ordinal — and what is left to apply is the pass's own count.
    /// </remarks>
    public EnrichableEmail Enrichable =>
        new(this.Id, this.Subject, this.ReceivedAt, [.. this.Passages.Take(MailEnrichmentPass.MaximumPassagesPerEmail)]);

    /// <summary>Reads what the message added as a store reads it out for a derivation: its leading characters, up to a bound.</summary>
    /// <param name="maximumCharacters">How many characters the store's query takes of one message.</param>
    /// <returns>The text, cut to the bound.</returns>
    public string TextWithin(int maximumCharacters) =>
        this.Text.Length <= maximumCharacters ? this.Text : this.Text[..maximumCharacters];

    /// <summary>Gets the text a judge holds the answer against: the subject and every passage.</summary>
    public string GroundingText =>
        string.Join("\n\n", [$"Subject: {this.Subject}", .. this.Passages.Select(static passage => passage.Text)]);

    /// <summary>Reads one message of the corpus.</summary>
    /// <param name="position">Where the message falls in delivery order, from zero, which is the order the archive numbers its files in.</param>
    /// <returns>The message.</returns>
    public static CorpusMessage At(int position) => All[position];

    /// <summary>Reads the conversation one message closes: its exchange up to and including it, oldest first.</summary>
    /// <param name="position">Where the closing message falls in delivery order.</param>
    /// <returns>The conversation as it stood when that message arrived.</returns>
    public static IReadOnlyList<CorpusMessage> ConversationUpTo(int position)
    {
        var closing = All[position];
        var exchange = Exchanges.Single(conversation => conversation.Contains(closing));

        return [.. exchange.TakeWhile(message => message != closing), closing];
    }

    /// <summary>Reads committed corpora as one run of conversations, numbering their messages from a first position.</summary>
    /// <param name="fileNames">The archives, in the order they are read: an archive read first keeps every position it numbers where it was.</param>
    /// <param name="firstPosition">The position the first message takes, from which every identifier is derived.</param>
    /// <returns>Every conversation, in delivery order, each in the order its messages were written.</returns>
    internal static IReadOnlyList<IReadOnlyList<CorpusMessage>> ReadArchives(IEnumerable<string> fileNames, int firstPosition)
    {
        var exchanges = fileNames.SelectMany(static fileName =>
        {
            using var archive = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "corpora", fileName));

            return CorpusArchive.Read(archive).Exchanges;
        }).ToList();

        // A message's position counts across every conversation before its own, which is the order the archive numbers
        // its files in and the one every identifier below is derived from.
        return
        [
            .. exchanges.Select(IReadOnlyList<CorpusMessage> (exchange, index) =>
            {
                var exchangePosition = firstPosition + exchanges.Take(index).Sum(static earlier => earlier.Count);

                return [.. exchange.Select((turn, offset) => Of(turn.Compose(), exchangePosition + offset))];
            }),
        ];
    }

    /// <summary>Reads one message the way a deployment would have stored it, under the identifier its position derives.</summary>
    /// <param name="message">The message, which this disposes.</param>
    /// <param name="position">Where it falls in delivery order, from which its identifier is derived.</param>
    /// <returns>The message, cut into its passages.</returns>
    internal static CorpusMessage Of(MimeMessage message, int position)
    {
        using (message)
        {
            using var stored = new MemoryStream();

            message.WriteTo(stored);

            // The pair a deployment derives, by its own extraction under its default bound: what the message carried,
            // and the reading with the quoted history cut off it.
            var body = MimeAttachmentClassifier.FindBody(message);
            var text = EmailBodyTextExtractor.Extract(
                body.TextParts,
                body.IsEncrypted,
                new MailSynchronizationOptions().MaxExtractedTextCharacters);

            var author = message.From.Mailboxes.FirstOrDefault();

            // Identifiers a deployment would have assigned in its store: distinct, and the same on every run, so an
            // answer's citation names the same message whether it was written today or read back from the cache.
            return new CorpusMessage(
                StoredEmailId.Create(new Guid(position + 1, 0, 0, new byte[8])),
                message.Subject ?? string.Empty,
                message.Date,
                author?.Address ?? string.Empty,
                author?.Name is { Length: > 0 } name ? name : null,
                [.. message.To.Mailboxes.Concat(message.Cc.Mailboxes).Select(static mailbox => mailbox.Address)],
                [.. MimeAttachmentClassifier.FindAttachmentParts(message).Select(static part => new CorpusAttachment(
                    MimeAttachmentClassifier.DeclaredFileNameOf(part)?.Value,
                    part.ContentType.MimeType))],
                text.TrimmedText ?? string.Empty,
                PassagesOf(text),
                stored.ToArray());
        }
    }

    private static List<EnrichablePassage> PassagesOf(ExtractedEmailText text)
    {
        // The rules chunk the trimmed reading, so handing the raw body twice would cut passages out of a reply's whole
        // history — which is exactly the text the corpus's own replies carry.
        var chunks = new DeterministicEmailTextChunker()
            .DeriveChunks(text, EmailChunkingRules.Current, EmbeddingInputBound.Default)
            .Chunks;

        // Nothing reaches a model through a passage identifier — a turn numbers its passages — so all they have to be
        // is distinct and the same on every run.
        return
        [
            .. chunks.Select(static chunk => new EnrichablePassage(
                EmailChunkId.Create(new Guid(chunk.Ordinal + 1, 0, 0, new byte[8])),
                chunk.Ordinal,
                chunk.Text)),
        ];
    }
}
