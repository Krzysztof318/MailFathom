// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Embeddings.Limits;

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>Declares what this deployment intends to embed with, and what one call to it may spend.</summary>
/// <remarks>
/// <para>
/// Declaring is free and activating is not, which is the split
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes: this section is where a reviewer, a chart, and a <c>git diff</c> can see which model an instance uses, and
/// editing it starts no spending. What turns a declaration into the profile stored vectors are attributed to is a
/// separate, explicit activation.
/// </para>
/// <para>
/// An absent section is a valid deployment rather than a startup failure. Nothing is embedded, semantic search is
/// unavailable, and lexical search serves as it always did — which is exactly the state an operator who has not chosen
/// a provider should be left in, rather than being made to choose one to start the service.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class EmbeddingOptions : IValidatableObject
{
    /// <summary>The characters one period may send where a deployment declares no ceiling of its own.</summary>
    /// <remarks>
    /// Fifty million characters a day is roughly twelve million tokens, which at the price of a small embedding model
    /// is small change and at the price of a large one is a figure worth noticing — which is the point of a default
    /// that binds. It embeds something like sixteen thousand ordinary messages a day, so an instance keeping up with
    /// arriving mail never meets it and one embedding a decade of archive is paced rather than surprised. An operator
    /// who wants an initial backfill finished sooner raises it deliberately, having seen the number.
    /// </remarks>
    public const long DefaultMaxInputCharactersPerPeriod = 50_000_000;

    /// <summary>The longest window the aggregate ceiling may be counted over.</summary>
    /// <remarks>
    /// A period longer than a month would let one burst spend a ceiling and leave embedding paused for weeks, which is
    /// a budget nobody would recognize as one from the outside.
    /// </remarks>
    private static readonly TimeSpan LongestSpendPeriod = TimeSpan.FromDays(31);

    /// <summary>The shortest window the aggregate ceiling may be counted over.</summary>
    /// <remarks>Below a minute the roll-over is faster than the pause it causes, so the ceiling would pace work instead of bounding it.</remarks>
    private static readonly TimeSpan ShortestSpendPeriod = TimeSpan.FromMinutes(1);

    /// <summary>Gets the endpoints in the order they are tried, all of them reaching one vector space.</summary>
    /// <remarks>
    /// An ordered chain rather than one endpoint, because an endpoint failing and a vector space changing are
    /// different events: one model served by two providers is the case this exists for. Every entry declares the same
    /// geometry and startup refuses a chain where they differ.
    /// </remarks>
    public IList<EmbeddingEndpointOptions> Endpoints { get; } = [];

    /// <summary>Gets or sets whether a vector wider than the declared dimension may be cut down to it.</summary>
    /// <remarks>
    /// Off by default, so a model wider than what the database indexes is refused at startup — naming the dimension
    /// and the ceiling — rather than quietly producing an instance whose semantic search never becomes fast. With it
    /// on, the declared dimension is what the profile records and what the stored vectors have, because a trimmed
    /// vector occupies a different space than the full one.
    /// </remarks>
    public bool AllowTrimVectors { get; set; }

    /// <summary>Gets or sets the greatest number of passages one request carries.</summary>
    /// <remarks>Bounded before the provider sees it, because a provider rejects an oversized batch after this deployment has already built and sent it.</remarks>
    [Range(1, 2048)]
    public int MaxPassagesPerRequest { get; set; } = 64;

    /// <summary>Gets or sets the time one request to one endpoint may take before it is abandoned.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets how many newly synchronized messages may wait to be embedded at once.</summary>
    /// <remarks>
    /// The bound is expressed rather than absorbed: a synchronization run that finds the backlog full stores the message
    /// and moves on instead of waiting, because it is holding an open mailbox session and a slow provider must not
    /// become a slow mailbox. Nothing is lost by that — the message and its passages are durable, and the backfill is
    /// what reaches mail the live path did not. Raising it buys a longer burst before that happens and costs memory
    /// proportional to the number of identifiers held, nothing more.
    /// </remarks>
    [Range(1, 1_000_000)]
    public int MaxQueuedEmails { get; set; } = EmailEmbeddingBacklogOptions.DefaultCapacity;

    /// <summary>Gets or sets how much of one message's extracted text is cut into passages.</summary>
    /// <remarks>
    /// The ceiling on what one message may cost. Per-item cost is not uniform — raw MIME is bounded in megabytes, so a
    /// single message can carry more text than a mailbox does in a month — and a message beyond this is bounded rather
    /// than refused: its opening is embedded and retrievable, and the length its text had is recorded on the message so
    /// that what was left out is a stored fact rather than something inferred from a chunk count.
    /// </remarks>
    [Range(1_000, 10_000_000)]
    public int MaxCharactersPerEmail { get; set; } = EmbeddingInputBound.DefaultMaximumCharacterCount;

    /// <summary>Gets or sets how many embedding requests one minute may carry, or zero to pace none.</summary>
    /// <remarks>
    /// The rate ceiling, which bounds neither cost nor concurrency. What one period may spend is
    /// <see cref="MaxInputCharactersPerPeriod" /> and how many calls may be in flight at once is the
    /// <c>AiProviderInvocation</c> resilience budget; this exists because a provider quota is stated per minute, and
    /// being refused for exceeding one costs an attempt, a retry, and a place in a circuit-breaker window.
    /// </remarks>
    [Range(0, 100_000)]
    public int MaxRequestsPerMinute { get; set; }

    /// <summary>Gets or sets the characters one period may send to a provider, or zero to bound nothing.</summary>
    /// <remarks>
    /// The aggregate ceiling, counted in the characters actually sent because that is what a provider's price is
    /// approximately proportional to and the one quantity this deployment can count exactly without carrying a model's
    /// own tokenizer. Reaching it pauses embedding until the period rolls over; nothing is dropped, because a passage
    /// with no vector is exactly what the backfill selects on.
    /// </remarks>
    public long MaxInputCharactersPerPeriod { get; set; } = DefaultMaxInputCharactersPerPeriod;

    /// <summary>Gets or sets the window the aggregate ceiling is counted over.</summary>
    /// <remarks>A fixed window anchored at the Unix epoch, so every restart agrees on where a period begins without anything being stored to say so.</remarks>
    public TimeSpan SpendPeriod { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Gets whether the deployment declared an embedding provider at all.</summary>
    public bool IsConfigured => this.Endpoints.Count > 0;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The ceilings are checked whether or not a chain was declared, unlike everything below them. Passages are cut
        // for every synchronized message on an instance that has chosen no provider — they are what a later activation
        // embeds — so a per-message ceiling nobody validated would be one an instance is already applying.
        foreach (var error in this.FindSpendCeilingErrors())
        {
            yield return error;
        }

        if (!this.IsConfigured)
        {
            yield break;
        }

        if (this.RequestTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "Embedding RequestTimeout is a positive duration, because an unbounded request would hold the work behind it open for as long as an endpoint stays silent.",
                [nameof(this.RequestTimeout)]);
        }

        foreach (var error in this.Endpoints.SelectMany(endpoint => endpoint.FindConfigurationErrors()))
        {
            yield return error;
        }

        foreach (var error in this.FindDuplicateAliases())
        {
            yield return error;
        }

        foreach (var error in this.FindGeometryErrors())
        {
            yield return error;
        }
    }

    /// <summary>Refuses an aggregate ceiling that could not bound a spend, or a period that could not carry one.</summary>
    private IEnumerable<ValidationResult> FindSpendCeilingErrors()
    {
        if (this.MaxInputCharactersPerPeriod < 0)
        {
            yield return new ValidationResult(
                "Embeddings MaxInputCharactersPerPeriod is zero or positive. Zero declares no aggregate ceiling at all, "
                + "which is a supported deployment; a negative one describes no budget.",
                [nameof(this.MaxInputCharactersPerPeriod)]);
        }

        if (this.SpendPeriod < ShortestSpendPeriod || this.SpendPeriod > LongestSpendPeriod)
        {
            yield return new ValidationResult(
                $"Embeddings SpendPeriod is between {ShortestSpendPeriod} and {LongestSpendPeriod}. Below that a "
                + "ceiling paces work rather than bounding it, and above it one burst leaves embedding paused for weeks.",
                [nameof(this.SpendPeriod)]);
        }
    }

    /// <summary>Refuses two endpoints sharing one alias, which is what a credential is resolved by.</summary>
    /// <remarks>
    /// Two endpoints under one name would resolve the same credential and share one resilience circuit, so an
    /// unreachable endpoint would open the breaker its twin is served through and neither could be read apart in a log.
    /// </remarks>
    private IEnumerable<ValidationResult> FindDuplicateAliases() => this.Endpoints
        .GroupBy(endpoint => endpoint.Alias.Trim(), StringComparer.OrdinalIgnoreCase)
        .Where(alias => alias.Count() > 1)
        .Select(alias => new ValidationResult(
            $"Embedding endpoints declare the alias '{alias.Key}' more than once. An alias names one endpoint, because it is what a credential, a circuit, and a log line are keyed by.",
            [nameof(this.Endpoints)]));

    /// <summary>Refuses a chain that would not serve one vector space, and a space the database cannot carry.</summary>
    /// <remarks>
    /// Both checks build the endpoints first, because an identity refuses a value no vector space could have — a blank
    /// model, a width that is not positive — and reporting that as an unhandled failure at startup would say less than
    /// the message the identity already carries.
    /// </remarks>
    private IEnumerable<ValidationResult> FindGeometryErrors()
    {
        List<EmbeddingEndpoint> endpoints = [];

        foreach (var declaration in this.Endpoints)
        {
            if (TryBuildEndpoint(declaration, out var endpoint, out var buildFailure))
            {
                endpoints.Add(endpoint);

                continue;
            }

            yield return new ValidationResult(
                $"Embedding endpoint '{declaration.Alias.Trim()}' does not describe a vector space: {buildFailure}",
                [nameof(this.Endpoints)]);

            yield break;
        }

        if (EmbeddingChainAgreement.FindDisagreement(endpoints) is { } disagreement)
        {
            yield return new ValidationResult(disagreement, [nameof(this.Endpoints)]);

            yield break;
        }

        if (!this.AllowTrimVectors && endpoints[0].Identity.Dimension > IndexableVectorWidth.GreatestIndexable)
        {
            yield return new ValidationResult(
                $"The declared embedding dimension {endpoints[0].Identity.Dimension} is above the {IndexableVectorWidth.GreatestIndexable} "
                + "an index covers, and AllowTrimVectors is off. Narrow the model, or turn AllowTrimVectors on and accept that the "
                + "profile records the narrowed width rather than the model's nominal one.",
                [nameof(this.Endpoints)]);
        }
    }

    /// <summary>Builds one endpoint, reporting rather than raising a declaration an identity refuses.</summary>
    /// <remarks>
    /// The identity already states exactly what is wrong with a blank model or a width that is not positive, so its
    /// message is carried into the validation result instead of being replaced by a weaker one — and the failure
    /// reaches an operator as configuration to correct rather than as an unhandled exception at startup.
    /// </remarks>
    private static bool TryBuildEndpoint(
        EmbeddingEndpointOptions declaration,
        out EmbeddingEndpoint endpoint,
        out string? failure)
    {
        try
        {
            endpoint = declaration.ToEndpoint();
            failure = null;

            return true;
        }
        catch (Exception buildFailure) when (buildFailure is ArgumentException or UriFormatException)
        {
            endpoint = null!;
            failure = buildFailure.Message;

            return false;
        }
    }
}
