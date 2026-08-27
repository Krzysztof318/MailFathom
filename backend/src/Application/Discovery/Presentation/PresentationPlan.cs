// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>What one Discover run produced: an ordered set of typed blocks, the sources behind them, and what the run knows about its own reach.</summary>
/// <remarks>
/// <para>
/// The contract between whatever composes an answer and whatever draws one. It is defined apart from both because two
/// properties of it have to hold whoever is at either end.
/// </para>
/// <para>
/// <strong>It is closed.</strong> A plan holds blocks from the catalogue in <see cref="PresentationBlockType" /> and
/// nothing else, and no part of it is markup, a template, an expression, or anything a client evaluates. That is what
/// lets the client draw a plan with ordinary typed UI, and it is what keeps a model from proposing a presentation
/// nobody wrote a renderer for.
/// </para>
/// <para>
/// <strong>It is versioned apart from the application.</strong> A deployment and a client are updated separately — a
/// browser bundle is served by the deployment, a desktop head is not — so a client will meet a plan produced by a
/// service ahead of it. <see cref="SchemaVersion" /> says which revision of this contract the plan was written against,
/// and each block carries the revision of its own type, so a client can draw the blocks it knows, say what it cannot,
/// and keep the rest of the run. Without both, the only safe answer to one unfamiliar block would be to discard the
/// whole thing.
/// </para>
/// <para>
/// Citations are declared once, here, and referred to by name from the blocks that rest on them — so two facts drawn
/// from one message are visibly the same source, and a client can list what a whole run rested on. The constructor
/// refuses a plan whose blocks name a citation it does not declare, which is the one way a citation contract fails
/// quietly rather than loudly.
/// </para>
/// <para>
/// A plan is composed from somebody's correspondence and is sensitive throughout: its texts are quoted or summarized
/// mail, its people are real people, and its citations name messages in a mailbox. It belongs in a response and
/// nowhere else — never in a log line, a span attribute, or an exception message.
/// </para>
/// </remarks>
public sealed record PresentationPlan
{
    /// <summary>The revision of this contract that this build writes and understands.</summary>
    /// <remarks>
    /// Raised when the shape of the plan itself changes — a member added to the plan, a rule about how blocks compose.
    /// A change confined to one block type raises that type's version instead, which is why the two numbers exist
    /// separately. It moves independently of the application's version, and a release that changes neither leaves it
    /// where it is.
    /// </remarks>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The greatest number of blocks one plan may hold.</summary>
    /// <remarks>
    /// A plan is an answer somebody reads, not a report. A run that composed more than this has widened rather than
    /// answered, and it says so with <see cref="PresentationLimitation.BlocksOmitted" /> instead of sending everything.
    /// </remarks>
    public const int MaxBlocks = 20;

    /// <summary>The greatest number of citations one plan may declare.</summary>
    public const int MaxCitations = 200;

    /// <summary>The greatest number of limitations a plan can state, which is one of each the catalogue holds.</summary>
    private static readonly int LimitationCount = Enum.GetValues<PresentationLimitation>().Length;

    /// <summary>Initializes what one run produced.</summary>
    /// <param name="schemaVersion">The revision of this contract the plan was written against.</param>
    /// <param name="blocks">The blocks, in the order they are read.</param>
    /// <param name="citations">The sources the blocks rest on, declared once each.</param>
    /// <param name="limitations">What the run knows about its own reach.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="blocks" />, <paramref name="citations" />, or <paramref name="limitations" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="schemaVersion" /> is below <c>1</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when a list is empty where it may not be or is oversized, a citation is declared twice, a limitation is named twice, or a block names a citation the plan does not declare.</exception>
    public PresentationPlan(
        int schemaVersion,
        IReadOnlyList<PresentationBlock> blocks,
        IReadOnlyList<PresentationCitation> citations,
        IReadOnlyList<PresentationLimitation> limitations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);

        var composedBlocks = PresentationRequirement.RequiredItems(blocks, MaxBlocks, nameof(blocks));
        var declaredCitations = PresentationRequirement.OptionalItems(citations, MaxCitations, nameof(citations));
        var statedLimitations = PresentationRequirement.OptionalItems(limitations, LimitationCount, nameof(limitations));

        EnsureCitationsAreDeclaredOnce(declaredCitations);
        EnsureEveryReferenceResolves(composedBlocks, declaredCitations);

        if (statedLimitations.Distinct().Count() != statedLimitations.Count)
        {
            throw new ArgumentException("A plan states each limitation once.", nameof(limitations));
        }

        this.SchemaVersion = schemaVersion;
        this.Blocks = composedBlocks;
        this.Citations = declaredCitations;
        this.Limitations = statedLimitations;
    }

    /// <summary>Gets the revision of this contract the plan was written against.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets the blocks, in the order they are read.</summary>
    public IReadOnlyList<PresentationBlock> Blocks { get; }

    /// <summary>Gets the sources the blocks rest on, declared once each.</summary>
    public IReadOnlyList<PresentationCitation> Citations { get; }

    /// <summary>Gets what the run knows about its own reach, which is empty where it reached everything it was asked about.</summary>
    public IReadOnlyList<PresentationLimitation> Limitations { get; }

    /// <summary>Composes a plan against the revision of the contract this build writes.</summary>
    /// <param name="blocks">The blocks, in the order they are read.</param>
    /// <param name="citations">The sources the blocks rest on, declared once each.</param>
    /// <param name="limitations">What the run knows about its own reach.</param>
    /// <returns>The plan, stamped with <see cref="CurrentSchemaVersion" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown for the reasons the constructor documents.</exception>
    /// <remarks>
    /// How a producer in this deployment writes a plan: the version is the build's rather than something a caller
    /// supplies, so nothing here can stamp a revision it did not write. The constructor stays reachable because a plan
    /// is also read — deserialization takes the version off the wire, which is the whole point of it being there.
    /// </remarks>
    public static PresentationPlan Compose(
        IReadOnlyList<PresentationBlock> blocks,
        IReadOnlyList<PresentationCitation> citations,
        IReadOnlyList<PresentationLimitation> limitations) =>
        new(CurrentSchemaVersion, blocks, citations, limitations);

    private static void EnsureCitationsAreDeclaredOnce(IReadOnlyList<PresentationCitation> citations)
    {
        if (citations.Any(citation => !citation.Id.IsSpecified))
        {
            throw new ArgumentException("A citation cannot be declared under the unspecified default.", nameof(citations));
        }

        if (citations.Select(citation => citation.Id).Distinct().Count() != citations.Count)
        {
            throw new ArgumentException("A plan declares each citation identifier once.", nameof(citations));
        }
    }

    private static void EnsureEveryReferenceResolves(
        IReadOnlyList<PresentationBlock> blocks,
        IReadOnlyList<PresentationCitation> citations)
    {
        var declared = citations.Select(citation => citation.Id).ToHashSet();

        var unresolved = blocks
            .SelectMany(block => block.ReferencedCitations)
            .Where(reference => !declared.Contains(reference))
            .Select(reference => reference.Value)
            .Distinct()
            .ToArray();

        if (unresolved.Length != 0)
        {
            throw new ArgumentException(
                $"The plan declares no citation named {string.Join(", ", unresolved)}.",
                nameof(blocks));
        }
    }
}
