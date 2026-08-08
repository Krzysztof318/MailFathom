// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Retrieval;

/// <summary>The mail one run may reach, and the record of what it reached.</summary>
/// <remarks>
/// <para>
/// This is where the scope stops being negotiable. The framework is handed a search that takes a query and nothing else,
/// because the accounts and folders were bound into this object when the run was composed: a model can write any query it
/// likes and every one of them is answered from the same scope. Nothing in an instruction, a retrieved message, or a tool
/// argument reaches the value, which is what makes the boundary structural rather than a rule the prompt asks for.
/// </para>
/// <para>
/// One instance serves one run. It records the passages it handed over so the answer can carry them, and that record is
/// per run for the same reason the scope is: one caller's retrieved mail must never appear beside another's.
/// </para>
/// </remarks>
internal sealed class ScopedMailKnowledgeRetrieval
{
    /// <summary>Names the tool the model calls to look mail up.</summary>
    /// <remarks>
    /// Named after what it does rather than after the framework's default <c>Search</c>, so a model holding several tools
    /// can tell what this one searches, and so a trace of a run reads as mail retrieval rather than as an unspecified
    /// lookup.
    /// </remarks>
    internal const string SearchToolName = "search_mail";

    private const string SearchToolDescription =
        "Searches the local copy of the mailbox for messages relevant to a query and returns short extracts from them.";

    private readonly IEmailKnowledgeSearch knowledgeSearch;
    private readonly MailboxScope scope;
    private readonly Lock gate = new();
    private readonly List<EmailKnowledgePassage> retrieved = [];

    /// <summary>Initializes the retrieval one run may make.</summary>
    /// <param name="knowledgeSearch">Finds the mail relevant to a query.</param>
    /// <param name="scope">The accounts and folders every retrieval of this run is answered from.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    internal ScopedMailKnowledgeRetrieval(IEmailKnowledgeSearch knowledgeSearch, MailboxScope scope)
    {
        ArgumentNullException.ThrowIfNull(knowledgeSearch);
        ArgumentNullException.ThrowIfNull(scope);

        this.knowledgeSearch = knowledgeSearch;
        this.scope = scope;
    }

    /// <summary>Gets the passages this run has handed over, in the order it handed them over.</summary>
    internal IReadOnlyList<EmailKnowledgePassage> Retrieved
    {
        get
        {
            lock (this.gate)
            {
                return [.. this.retrieved];
            }
        }
    }

    /// <summary>Builds the context provider the framework retrieves through.</summary>
    /// <param name="loggerFactory">Creates the logger the provider records its own decisions through.</param>
    /// <returns>The provider, which searches only when the model asks it to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="loggerFactory" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// On demand rather than before every call, which is the difference between a question that needed no mail costing
    /// nothing and one that drags a mailbox through a provider to answer "what can you do". The model decides it needs
    /// context and asks; nothing is pushed at it.
    /// </remarks>
    internal TextSearchProvider CreateContextProvider(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var options = new TextSearchProviderOptions
        {
            SearchTime = TextSearchProviderOptions.TextSearchBehavior.OnDemandFunctionCalling,
            FunctionToolName = SearchToolName,
            FunctionToolDescription = SearchToolDescription,

            // Replaces the framework's own formatting, which writes each result as a labelled paragraph between dashed
            // separators and closes with an instruction of its own. Retrieved mail written that way is prose in the same
            // voice as an instruction, and a message imitating one of those separators is indistinguishable from it.
            ContextFormatter = FormatRetrieved,

            // The framework's own switch for putting queries and retrieved text into its logs. It defaults to off and is
            // set here anyway, because what it would emit is somebody's question and extracts of their mail, and a
            // default is a thing a package update may change.
            EnableSensitiveTelemetryData = false,
        };

        return new TextSearchProvider(this.SearchAsync, options, loggerFactory);
    }

    /// <summary>Answers one lookup the model asked for.</summary>
    /// <remarks>
    /// The result carries the passage itself as its raw representation rather than only the text, so whatever formats the
    /// context reaches the message identity and the source coordinates without searching for them again.
    /// </remarks>
    private async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var passages = await this.knowledgeSearch.FindPassagesAsync(this.scope, query, cancellationToken);

        lock (this.gate)
        {
            this.retrieved.AddRange(passages);
        }

        return
        [
            .. passages.Select(static passage => new TextSearchProvider.TextSearchResult
            {
                Text = passage.Text,
                SourceName = passage.StoredEmailId.ToString(),
                RawRepresentation = passage,
            }),
        ];
    }

    /// <summary>Writes what one lookup found into the envelope the model reads it inside.</summary>
    /// <remarks>
    /// Every result of this run was built above and carries its passage, so the formatter reaches the message identity
    /// and the source coordinates rather than the flattened strings the framework's own result type carries. The cast
    /// asserts that rather than filtering on it: a result arriving from anywhere else would otherwise be dropped from
    /// the envelope while <see cref="Retrieved" /> still recorded it, leaving the answer citing a message the model was
    /// never shown.
    /// </remarks>
    private static string FormatRetrieved(IList<TextSearchProvider.TextSearchResult> results) =>
        RetrievedMailContextFormatter.Format(
            [.. results.Select(static result => result.RawRepresentation).Cast<EmailKnowledgePassage>()]);
}
