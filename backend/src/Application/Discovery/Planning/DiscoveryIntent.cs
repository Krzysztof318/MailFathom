// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;

namespace MailFathom.Application.Discovery.Planning;

/// <summary>What a question is asking for, and the block a result answering it opens with.</summary>
/// <remarks>
/// <para>
/// A person asking about their mail never picks a mode and never chooses a presentation. The shape of the result
/// follows from the shape of the question, and this is the closed set of shapes that mapping is defined over: a
/// comparison arrives as a table because it was a comparison, not because somebody asked for a table.
/// </para>
/// <para>
/// It is a closed enumeration rather than a plain enum because each member carries the block it opens with, and because
/// <see cref="Identity" /> is the name a model answers with — a published identity that has to survive a rename of the
/// member beside it.
/// </para>
/// <para>
/// <see cref="Unclassified" /> is what makes the set total. A question fitting none of the four named kinds is ordinary
/// rather than a refusal, and it is answered the way a question with no particular shape should be: with an answer and
/// the evidence behind it.
/// </para>
/// </remarks>
public readonly record struct DiscoveryIntent
{
    /// <summary>The identity of the intent that asks for a specific fact.</summary>
    public const string FindFactIdentity = "findFact";

    /// <summary>The identity of the intent that asks what changed over time.</summary>
    public const string TrackChangeIdentity = "trackChange";

    /// <summary>The identity of the intent that asks for offers or terms to be compared.</summary>
    public const string CompareTermsIdentity = "compareTerms";

    /// <summary>The identity of the intent that asks for documents.</summary>
    public const string FindDocumentsIdentity = "findDocuments";

    /// <summary>The identity of the intent a question fitting none of the named kinds is read as.</summary>
    public const string UnclassifiedIdentity = "unclassified";

    private readonly string? identity;
    private readonly PresentationBlockType opensWith;

    private DiscoveryIntent(string identity, PresentationBlockType opensWith)
    {
        this.identity = identity;
        this.opensWith = opensWith;
    }

    /// <summary>Gets the intent of a question asking what something is, which opens with the answer and its evidence.</summary>
    public static DiscoveryIntent FindFact { get; } = new(FindFactIdentity, PresentationBlockType.Answer);

    /// <summary>Gets the intent of a question asking how something changed, which opens with the sequence of events.</summary>
    public static DiscoveryIntent TrackChange { get; } = new(TrackChangeIdentity, PresentationBlockType.Timeline);

    /// <summary>Gets the intent of a question setting several offers or terms against each other, which opens with the table comparing them.</summary>
    public static DiscoveryIntent CompareTerms { get; } = new(CompareTermsIdentity, PresentationBlockType.FactTable);

    /// <summary>Gets the intent of a question looking for files, which opens with the documents themselves.</summary>
    /// <remarks>
    /// A document is found by what it says as well as by what it is called, because the same retrieval paths a plan
    /// reads reach a document attachment's own text.
    /// </remarks>
    public static DiscoveryIntent FindDocuments { get; } =
        new(FindDocumentsIdentity, PresentationBlockType.AttachmentGallery);

    /// <summary>Gets the intent a question fitting none of the four named kinds carries.</summary>
    public static DiscoveryIntent Unclassified { get; } = new(UnclassifiedIdentity, PresentationBlockType.Answer);

    /// <summary>Gets every intent this catalogue holds, in the order the product describes them.</summary>
    public static IReadOnlyList<DiscoveryIntent> All { get; } =
    [
        FindFact,
        TrackChange,
        CompareTerms,
        FindDocuments,
        Unclassified,
    ];

    /// <summary>Gets whether this value names an intent rather than being the default of the struct.</summary>
    public bool IsSpecified => this.identity is not null;

    /// <summary>Gets the name this intent is written and read under.</summary>
    /// <exception cref="InvalidOperationException">The value is the default of the struct.</exception>
    public string Identity => this.identity
        ?? throw new InvalidOperationException("The value is the default of the struct and names no intent.");

    /// <summary>Gets the block a result answering a question of this intent opens with.</summary>
    /// <exception cref="InvalidOperationException">The value is the default of the struct.</exception>
    public PresentationBlockType OpensWith => this.identity is null
        ? throw new InvalidOperationException("The value is the default of the struct and opens with no block.")
        : this.opensWith;

    /// <summary>Reads an intent from the name it is written under.</summary>
    /// <param name="identity">The name to read, which may be surrounded by whitespace.</param>
    /// <param name="intent">The intent the name means, or the default of the struct when it means none.</param>
    /// <returns><see langword="true" /> when the name is one this catalogue holds.</returns>
    /// <remarks>
    /// A name this catalogue does not hold is not an error here. It is what a model answering with something else
    /// produces, and the planner reads that as <see cref="Unclassified" /> rather than as a failed run.
    /// </remarks>
    public static bool TryParse(string? identity, out DiscoveryIntent intent)
    {
        intent = default;

        if (string.IsNullOrWhiteSpace(identity))
        {
            return false;
        }

        var normalized = identity.Trim();
        intent = All.FirstOrDefault(candidate =>
            string.Equals(candidate.Identity, normalized, StringComparison.OrdinalIgnoreCase));

        return intent.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.identity ?? "(unspecified)";
}
