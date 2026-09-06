// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;

namespace MailFathom.Application.Discovery.Planning;

/// <summary>What one question decided: what it is asking for, what to retrieve, and what shape the result takes.</summary>
/// <remarks>
/// <para>
/// The two halves are one decision made over one reading of the question, which is why they are derived together and
/// carried together. A run that retrieved first and then asked separately how to present what it found would present
/// whatever it happened to retrieve.
/// </para>
/// <para>
/// <strong>The composition is derived rather than chosen.</strong> A model reads the question and answers with an
/// intent and the lookups that intent implies; which blocks follow from that intent is
/// <see cref="ComposedFor" />'s to say, in code. That is what makes the product's mapping an invariant rather than an
/// instruction a model may drift from, and it is what makes the same question over the same scope and corpus yield the
/// same composition — the block types depend on the intent alone, so nothing about a provider's wording reaches them.
/// </para>
/// </remarks>
public sealed record DiscoveryRunPlan
{
    private DiscoveryRunPlan(
        DiscoveryIntent intent,
        RetrievalPlan retrieval,
        IReadOnlyList<PresentationBlockType> composition)
    {
        this.Intent = intent;
        this.Retrieval = retrieval;
        this.Composition = composition;
    }

    /// <summary>Gets what the question is asking for.</summary>
    public DiscoveryIntent Intent { get; }

    /// <summary>Gets how the question is looked for.</summary>
    public RetrievalPlan Retrieval { get; }

    /// <summary>Gets the block types the result is composed of, in the order they are presented.</summary>
    /// <remarks>
    /// It opens with the block <see cref="DiscoveryIntent.OpensWith" /> names, so a comparison opens as a table and a
    /// search for documents opens as the documents. The evidence follows every one of them, because a result whose
    /// sources are not on it is one nobody can check.
    /// </remarks>
    public IReadOnlyList<PresentationBlockType> Composition { get; }

    /// <summary>Composes the plan for an intent and the retrieval it implies.</summary>
    /// <param name="intent">What the question is asking for.</param>
    /// <param name="retrieval">How the question is looked for.</param>
    /// <returns>The plan, with the composition derived from the intent.</returns>
    /// <exception cref="ArgumentException"><paramref name="intent" /> is the default of its struct and names nothing.</exception>
    public static DiscoveryRunPlan Compose(DiscoveryIntent intent, RetrievalPlan retrieval)
    {
        ArgumentNullException.ThrowIfNull(retrieval);

        if (!intent.IsSpecified)
        {
            throw new ArgumentException("A plan cannot be composed for an intent that names nothing.", nameof(intent));
        }

        return new DiscoveryRunPlan(intent, retrieval, ComposedFor(intent));
    }

    /// <summary>Says which blocks a result of one intent is composed of.</summary>
    /// <param name="intent">The intent the question was read as.</param>
    /// <returns>The block types, in presentation order.</returns>
    /// <remarks>
    /// The whole of the product's intent-to-presentation mapping, in one expression. Every composition ends with the
    /// evidence rather than only the ones whose opening block omits its own sources, so a reader checks a result the
    /// same way whatever was asked.
    /// </remarks>
    private static IReadOnlyList<PresentationBlockType> ComposedFor(DiscoveryIntent intent) =>
        [intent.OpensWith, PresentationBlockType.EvidenceList];
}
