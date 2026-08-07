// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Embeddings;

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

    /// <summary>Gets whether the deployment declared an embedding provider at all.</summary>
    public bool IsConfigured => this.Endpoints.Count > 0;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
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
