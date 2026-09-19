// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Evaluations.Corpus;

namespace MailFathom.Evaluations.Answering;

/// <summary>Answers the answering agent's lookups from the synthetic corpus, held in memory as one inbox.</summary>
/// <remarks>
/// <para>
/// What is measured is the agent — which lookups it writes, which filters it reaches for, and what it does with what
/// comes back — so the search only has to honour the tool's contract: every filter the tool publishes narrows exactly
/// as it says, and the words match the way a deployment's lexical search matches them, every word required unless
/// <c>OR</c> offers an alternative. That last part is what keeps a lookup from flooding the run: admitting any message
/// carrying any word fills a whole window with mail that barely matches and spends the run's retrieval allowance on
/// the first lookup. It answers through the same
/// <see cref="EmailKnowledgeLookup" /> a deployment's search returns, one passage per message, cut into the extracts
/// <see cref="EmailSearchSnippetBounds.Default" /> allows and bounded the way <see cref="EmailKnowledgeBounds.Default" />
/// bounds one.
/// </para>
/// <para>
/// The corpus carries no server state, so every message reads as unread, unflagged, and carrying no keyword.
/// </para>
/// </remarks>
/// <param name="corpus">The messages the inbox holds.</param>
internal sealed partial class CorpusKnowledgeSearch(IReadOnlyList<CorpusMessage> corpus) : IEmailKnowledgeSearch
{
    /// <summary>The account the corpus is delivered to.</summary>
    public static readonly MailAccountId Account = MailAccountId.Create("owner");

    /// <summary>The folder the corpus is delivered to.</summary>
    public static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    /// <summary>Words too common to say anything about which message a query means.</summary>
    private static readonly HashSet<string> CommonWords = new(
        ["the", "and", "for", "from", "with", "what", "which", "when", "who", "did", "was", "were", "about", "that", "this", "have", "has", "you", "your", "our", "any", "are"],
        StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentQueue<string> queries = new();

    /// <summary>Gets the scope every lookup of a run is answered from: the one inbox.</summary>
    public static MailboxScope Scope { get; } = MailboxScope.Create([Account], [new MailFolderIdentity(Account, Inbox)]);

    /// <summary>Gets how many lookups this search answered.</summary>
    public int Lookups => this.queries.Count;

    /// <summary>Gets the words of every lookup this search answered, in the order they arrived.</summary>
    public IReadOnlyList<string> Queries => [.. this.queries];

    /// <inheritdoc />
    public Task<EmailKnowledgeLookup> FindPassagesAsync(
        MailboxScope scope,
        EmailKnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        this.queries.Enqueue(query.QueryText);

        var (alternatives, excluded) = TermsOf(query.QueryText);
        var wanted = alternatives.SelectMany(static terms => terms).ToList();

        // ponytail: substring matching over websearch_to_tsquery's AND and OR, not the deployment's PostgreSQL full-text
        // search — no tokenizer, no phrase adjacency, ranked by words carried rather than ts_rank; run the scenarios over
        // the real search in the integration harness if the ranking itself is measured.
        var found = corpus
            .Where(message => Admits(message, query))
            .Where(message => !excluded.Any(term => Mentions(message, term)))
            .Where(message => alternatives.Count is 0 || alternatives.Any(terms => terms.All(term => Mentions(message, term))))
            .Select(message => (Message: message, Score: wanted.Count(term => Mentions(message, term))))
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.Message.ReceivedAt)
            .Take(EmailKnowledgeBounds.Default.MaximumPassages)
            .Select(candidate => PassageOf(candidate.Message, wanted))
            .ToList();

        return Task.FromResult(EmailKnowledgeLookup.Unfiltered(found, EmailSearchRetrievalMode.Lexical));
    }

    private static bool Admits(CorpusMessage message, EmailKnowledgeQuery query) =>
        (query.SenderAddress is null || string.Equals(message.Sender, query.SenderAddress, StringComparison.OrdinalIgnoreCase))
        && (query.RecipientAddress is null || message.Recipients.Contains(query.RecipientAddress, StringComparer.OrdinalIgnoreCase))
        && (query.SubjectFragment is null || message.Subject.Contains(query.SubjectFragment, StringComparison.OrdinalIgnoreCase))
        && (query.ReceivedOnOrAfter is not { } after || message.ReceivedAt >= after)
        && (query.ReceivedBefore is not { } before || message.ReceivedAt < before)
        && (query.HasAttachments is not { } attached || message.HasAttachments == attached)
        && query.IsRemotelySeen is not true
        && query.IsRemotelyFlagged is not true
        && query.Keyword is null;

    private static bool Mentions(CorpusMessage message, string term) =>
        message.GroundingText.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the alternatives a query offers, each the words it requires together, and the words it excludes, keeping a quoted phrase whole.</summary>
    private static (IReadOnlyList<IReadOnlyList<string>> Alternatives, IReadOnlyList<string> Excluded) TermsOf(string queryText)
    {
        List<IReadOnlyList<string>> alternatives = [];
        List<string> current = [];
        List<string> excluded = [];

        foreach (Match token in QueryToken().Matches(queryText))
        {
            if (string.Equals(token.Value, "OR", StringComparison.OrdinalIgnoreCase))
            {
                EndAlternative();
                continue;
            }

            var excludes = token.Value.StartsWith('-');
            var term = token.Value.TrimStart('-').Trim('"').Trim('.', ',', ';', ':', '!', '?', '(', ')', '\'');

            if (term.Length < 2 || CommonWords.Contains(term))
            {
                continue;
            }

            (excludes ? excluded : current).Add(term);
        }

        EndAlternative();

        return (alternatives, excluded);

        void EndAlternative()
        {
            if (current.Count > 0)
            {
                alternatives.Add(current);
                current = [];
            }
        }
    }

    /// <summary>Cuts the message down to the extracts a deployment's <c>ts_headline</c> returns: a few short windows around the wanted words, those carrying the most of them first.</summary>
    /// <remarks>
    /// The size is what matters here rather than the exact words: a whole passage per message is several times what a
    /// deployment sends, so twenty of them spend a run's whole retrieval allowance on one lookup, and an extract cut from
    /// the start of a message rather than around the match misses the sentence that carries the answer.
    /// </remarks>
    private static EmailKnowledgePassage PassageOf(CorpusMessage message, IReadOnlyList<string> wanted)
    {
        var snippets = EmailSearchSnippetBounds.Default;

        // ponytail: one window per wanted word's first occurrence in a passage, scored by the words it carries — not
        // ts_headline's cover density; measure the ranking over the real search if an extract's exact words matter.
        var fragments = message.Passages
            .SelectMany(passage => wanted
                .Select(term => passage.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase))
                .Where(static position => position >= 0)
                .Select(position => WindowAround(passage.Text, position, snippets.WordsPerSnippet)))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(fragment => wanted.Count(term => fragment.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(snippets.SnippetsPerEmail)
            .DefaultIfEmpty(message.Passages.Count is 0 ? string.Empty : WindowAround(message.Passages[0].Text, 0, snippets.WordsPerSnippet));

        var text = string.Join('\n', fragments);
        var limit = EmailKnowledgeBounds.Default.MaximumCharactersPerPassage;

        return new EmailKnowledgePassage
        {
            StoredEmailId = message.Id,
            AccountId = Account,
            FolderAlias = Inbox,
            Subject = message.Subject,
            ReceivedAt = message.ReceivedAt,
            SenderVerification = SenderVerification.NotEstablished,
            MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
            Text = text.Length > limit ? text[..limit] : text,
        };
    }

    /// <summary>Takes the words around a position, half before it and half from it.</summary>
    private static string WindowAround(string text, int position, int words)
    {
        var before = text[..position].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).TakeLast(words / 2);
        var after = text[position..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(words - (words / 2));

        return string.Join(' ', before.Concat(after));
    }

    [GeneratedRegex("-?\"[^\"]+\"|\\S+")]
    private static partial Regex QueryToken();
}
