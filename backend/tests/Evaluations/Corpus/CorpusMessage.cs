// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chunking;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.SyntheticMail.Corpus;
using MimeKit;

namespace MailFathom.Evaluations.Corpus;

/// <summary>One message of the committed synthetic corpus, cut into the passages a deployment would have stored for it.</summary>
/// <remarks>
/// <para>
/// The corpus is the only input a scenario sends a provider. Every message in it was written by a model for this
/// repository, every address sits under a reserved domain, and nobody received any of it — which is what lets a
/// scenario, its cached answers, and its report be published.
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
/// <param name="Recipients">The addresses in its To and Cc headers.</param>
/// <param name="HasAttachments">Whether it carries an attachment.</param>
/// <param name="Passages">The passages, in order.</param>
internal sealed record CorpusMessage(
    StoredEmailId Id,
    string Subject,
    DateTimeOffset ReceivedAt,
    string Sender,
    IReadOnlyList<string> Recipients,
    bool HasAttachments,
    IReadOnlyList<EnrichablePassage> Passages)
{
    /// <summary>The bound a deployment extracts a body under by default.</summary>
    private const int MaximumBodyCharacters = 100_000;

    private static readonly string ArchivePath = Path.Combine(AppContext.BaseDirectory, "corpora", "office-en.zip");

    private static readonly Lazy<IReadOnlyList<CorpusMessage>> Delivered = new(ReadArchive);

    /// <summary>Gets every message of the corpus, in delivery order.</summary>
    public static IReadOnlyList<CorpusMessage> All => Delivered.Value;

    /// <summary>Gets the text a judge holds the answer against: the subject and every passage.</summary>
    public string GroundingText =>
        string.Join("\n\n", [$"Subject: {this.Subject}", .. this.Passages.Select(static passage => passage.Text)]);

    /// <summary>Reads one message of the corpus.</summary>
    /// <param name="position">Where the message falls in delivery order, from zero, which is the order the archive numbers its files in.</param>
    /// <returns>The message.</returns>
    public static CorpusMessage At(int position) => All[position];

    private static IReadOnlyList<CorpusMessage> ReadArchive()
    {
        using var archive = File.OpenRead(ArchivePath);

        return
        [
            .. CorpusArchive.Read(archive)
                .Exchanges
                .SelectMany(static exchange => exchange)
                .Select(static (turn, position) => Read(turn.Compose(), position)),
        ];
    }

    private static CorpusMessage Read(MimeMessage message, int position)
    {
        using (message)
        {
            // Identifiers a deployment would have assigned in its store: distinct, and the same on every run, so an
            // answer's citation names the same message whether it was written today or read back from the cache.
            return new CorpusMessage(
                StoredEmailId.Create(new Guid(position + 1, 0, 0, new byte[8])),
                message.Subject ?? string.Empty,
                message.Date,
                message.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty,
                [.. message.To.Mailboxes.Concat(message.Cc.Mailboxes).Select(static mailbox => mailbox.Address)],
                message.Attachments.Any(),
                PassagesOf(message));
        }
    }

    private static List<EnrichablePassage> PassagesOf(MimeMessage message)
    {
        // The same pair a deployment derives: what the message carried, and the reading with the quoted history cut
        // off it. The rules chunk the trimmed one, so handing the raw body twice would cut passages out of a reply's
        // whole history — which is exactly the text the corpus's own replies carry. A message with no plain-text part
        // is read from its markup the way a deployment reads it, rather than being taken for one that said nothing.
        var text = message.TextBody is { } plain
            ? ExtractedEmailText.FromPlainTextBody(plain, QuotedHistoryTrimmer.Trim(plain))
            : DerivedFromMarkup(message.HtmlBody ?? string.Empty);

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

    private static ExtractedEmailText DerivedFromMarkup(string html)
    {
        var derived = HtmlBodyTextReader.ReadDisplayedText(html, MaximumBodyCharacters).Trim();

        return derived.Length is 0
            ? ExtractedEmailText.NoTextualBody
            : ExtractedEmailText.DerivedFromHtmlBody(derived, QuotedHistoryTrimmer.Trim(derived));
    }
}
