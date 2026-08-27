// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>What the correspondence does for one block: which sources it rests on, and how far they carry it.</summary>
/// <remarks>
/// <para>
/// Every block carries one of these, which is what makes citation part of the contract rather than a habit a producer
/// may keep. The constructor holds the support to the citation count that gives it meaning: a supported claim names at
/// least one source, a conflicting one names at least two — a disagreement needs two sides — and an unsupported one
/// names none, because a source that backed it would make it supported.
/// </para>
/// <para>
/// That rule is the reason the type exists rather than three loose properties on each block. A block asserting support
/// while citing nothing is exactly the failure a citation contract is written to prevent, and it is far cheaper to
/// refuse it here than to find it in a rendered answer.
/// </para>
/// </remarks>
public sealed record PresentationEvidence
{
    /// <summary>The greatest number of citations one block may rest on.</summary>
    /// <remarks>
    /// A bound rather than a preference: a block is what a person reads, and an answer resting on forty messages is a
    /// retrieval that failed to narrow rather than an answer worth forty sources.
    /// </remarks>
    public const int MaxCitations = 24;

    /// <summary>Initializes what the correspondence does for one block.</summary>
    /// <param name="support">What the correspondence does for what the block states.</param>
    /// <param name="citations">The sources the block rests on, in the order they are worth reading.</param>
    /// <param name="freshness">How current the data behind the block was.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="citations" /> or <paramref name="freshness" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the citation count does not match the support, a citation is unspecified, a citation is named twice, or there are more than <see cref="MaxCitations" /> of them.</exception>
    public PresentationEvidence(
        PresentationSupport support,
        IReadOnlyList<PresentationCitationId> citations,
        PresentationFreshness freshness)
    {
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(freshness);

        if (citations.Count > MaxCitations)
        {
            throw new ArgumentException($"A block rests on at most {MaxCitations} citations.", nameof(citations));
        }

        if (citations.Any(citation => !citation.IsSpecified))
        {
            throw new ArgumentException("A citation reference cannot be the unspecified default.", nameof(citations));
        }

        if (citations.Distinct().Count() != citations.Count)
        {
            throw new ArgumentException("A block names each of its citations once.", nameof(citations));
        }

        EnsureCitationCountMatches(support, citations);

        this.Support = support;
        this.Citations = [.. citations];
        this.Freshness = freshness;
    }

    /// <summary>Gets what the correspondence does for what the block states.</summary>
    public PresentationSupport Support { get; }

    /// <summary>Gets the sources the block rests on, in the order they are worth reading.</summary>
    public IReadOnlyList<PresentationCitationId> Citations { get; }

    /// <summary>Gets how current the data behind the block was.</summary>
    public PresentationFreshness Freshness { get; }

    /// <summary>States that nothing found backs what a block says.</summary>
    /// <param name="freshness">How current the data the run did read was.</param>
    /// <returns>Evidence naming no source, because there is none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="freshness" /> is <see langword="null" />.</exception>
    public static PresentationEvidence Unsupported(PresentationFreshness freshness) =>
        new(PresentationSupport.Unsupported, [], freshness);

    private static void EnsureCitationCountMatches(
        PresentationSupport support,
        IReadOnlyList<PresentationCitationId> citations)
    {
        var refusal = support switch
        {
            PresentationSupport.Supported when citations.Count == 0 =>
                "A supported block names the source that backs it.",
            PresentationSupport.Unsupported when citations.Count != 0 =>
                "A block naming a source is supported by it rather than unsupported.",
            PresentationSupport.Conflicting when citations.Count < 2 =>
                "A conflict is between sources, so a conflicting block names at least two of them.",
            _ => null,
        };

        if (refusal is not null)
        {
            throw new ArgumentException(refusal, nameof(citations));
        }
    }
}
