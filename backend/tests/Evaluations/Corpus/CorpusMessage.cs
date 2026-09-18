// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chunking;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.SyntheticMail.Corpus;

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
/// <param name="Subject">The subject.</param>
/// <param name="ReceivedAt">When the message is dated, which is what a relative date in it resolves against.</param>
/// <param name="Passages">The passages, in order.</param>
internal sealed record CorpusMessage(string Subject, DateTimeOffset ReceivedAt, IReadOnlyList<EnrichablePassage> Passages)
{
    private static readonly string ArchivePath = Path.Combine(AppContext.BaseDirectory, "corpora", "office-en.zip");

    /// <summary>Gets the text a judge holds the answer against: the subject and every passage.</summary>
    public string GroundingText =>
        string.Join("\n\n", [$"Subject: {this.Subject}", .. this.Passages.Select(static passage => passage.Text)]);

    /// <summary>Reads one message of the corpus.</summary>
    /// <param name="position">Where the message falls in delivery order, from zero, which is the order the archive numbers its files in.</param>
    /// <returns>The message.</returns>
    public static CorpusMessage At(int position)
    {
        using var archive = File.OpenRead(ArchivePath);

        var turn = CorpusArchive.Read(archive).Exchanges.SelectMany(static exchange => exchange).ElementAt(position);
        var message = turn.Compose();
        var body = message.TextBody ?? string.Empty;

        // The same pair a deployment derives: what the message carried, and the reading with the quoted history cut
        // off it. The rules chunk the trimmed one, so handing the raw body twice would cut passages out of a reply's
        // whole history — which is exactly the text the corpus's own replies carry.
        var text = ExtractedEmailText.FromPlainTextBody(body, QuotedHistoryTrimmer.Trim(body));

        var chunks = new DeterministicEmailTextChunker()
            .DeriveChunks(text, EmailChunkingRules.Current, EmbeddingInputBound.Default)
            .Chunks;

        // Identifiers a deployment would have assigned in its store. Nothing reaches a model through them — a turn numbers
        // its passages — so all they have to be is distinct and the same on every run.
        return new CorpusMessage(
            message.Subject ?? string.Empty,
            message.Date,
            [.. chunks.Select(static chunk => new EnrichablePassage(
                EmailChunkId.Create(new Guid(chunk.Ordinal + 1, 0, 0, new byte[8])),
                chunk.Ordinal,
                chunk.Text))]);
    }
}
