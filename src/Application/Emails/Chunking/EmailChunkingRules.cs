// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Chunking;

/// <summary>The boundaries one message's extracted text is cut along, and the version those boundaries are known by.</summary>
/// <remarks>
/// <para>
/// These rules are the whole of what a chunker may vary, which is what lets <see cref="EmailChunkContentHash" /> cover
/// them: a chunk's identity states the text it holds and the rules that produced it, so a vector hanging on that chunk
/// is attributable to both. See <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>,
/// which places the boundary rules here rather than in the embedding profile, so re-cutting a mailbox costs local
/// computation instead of a provider bill.
/// </para>
/// <para>
/// <see cref="RuleSetVersion" /> is not what makes the rules take effect and nothing reads it to decide correctness —
/// the hash already covers every field below. It exists so a backfill can be asked how much of a mailbox is still cut
/// to the previous rules, which is a question a hash has no answer to. Change any other member and increment it in the
/// same edit.
/// </para>
/// <para>
/// Deliberately not a record: two rule sets are never compared, and the value equality a record would publish would
/// compare the separator ladder by reference and report two identical rule sets as different.
/// </para>
/// </remarks>
public sealed class EmailChunkingRules
{
    /// <summary>The separators the current rules break along, from the strongest to the weakest.</summary>
    /// <remarks>
    /// Extraction has already unified line endings and removed every control character except the line break and the
    /// tab, so a paragraph break is exactly two line feeds and the ladder needs no carriage-return variants.
    /// </remarks>
    private static readonly string[] CurrentBoundarySeparators = ["\n\n", "\n", " "];

    private EmailChunkingRules(
        int ruleSetVersion,
        int targetCharacterCount,
        int minimumCharacterCount,
        int overlapCharacterCount,
        IReadOnlyList<string> boundarySeparators,
        EmailChunkSourceForm sourceForm)
    {
        this.RuleSetVersion = ruleSetVersion;
        this.TargetCharacterCount = targetCharacterCount;
        this.MinimumCharacterCount = minimumCharacterCount;
        this.OverlapCharacterCount = overlapCharacterCount;
        this.BoundarySeparators = boundarySeparators;
        this.SourceForm = sourceForm;
    }

    /// <summary>Gets the rules every chunk MailFathom derives today is cut to.</summary>
    /// <remarks>
    /// A thousand characters is a few paragraphs of mail, which is small enough that a retrieved passage answers a
    /// question on its own and large enough that a sentence keeps the context that disambiguates it. The overlap is a
    /// fifth of that, so a statement straddling a boundary is whole in one of the two chunks that share it, and the
    /// minimum keeps a separator near the start of a window from cutting a chunk too short to mean anything.
    /// </remarks>
    public static EmailChunkingRules Current { get; } = new(
        ruleSetVersion: 1,
        targetCharacterCount: 1000,
        minimumCharacterCount: 250,
        overlapCharacterCount: 200,
        CurrentBoundarySeparators,
        EmailChunkSourceForm.TrimmedText);

    /// <summary>Gets the version these rules are recorded under, which a chunk carries beside its hash.</summary>
    public int RuleSetVersion { get; }

    /// <summary>Gets the greatest number of characters one chunk may hold.</summary>
    public int TargetCharacterCount { get; }

    /// <summary>Gets the number of characters a chunk must reach before a separator inside the window may end it.</summary>
    /// <remarks>
    /// Without it, a blank line early in a long paragraph would end a chunk after a handful of words, and the message
    /// would be cut into fragments too small to retrieve on. A window that finds no qualifying separator is cut at its
    /// end instead, so the bound shortens no chunk — it only refuses a break that would.
    /// </remarks>
    public int MinimumCharacterCount { get; }

    /// <summary>Gets how far back into the previous chunk the next one starts.</summary>
    public int OverlapCharacterCount { get; }

    /// <summary>Gets the separators a chunk is preferably ended after, from the strongest to the weakest.</summary>
    public IReadOnlyList<string> BoundarySeparators { get; }

    /// <summary>Gets which reading of the message body the chunks are cut from.</summary>
    public EmailChunkSourceForm SourceForm { get; }

    /// <summary>Builds a rule set, refusing one that could not cut text into chunks that make progress.</summary>
    /// <param name="ruleSetVersion">The version these rules are recorded under.</param>
    /// <param name="targetCharacterCount">The greatest number of characters one chunk may hold.</param>
    /// <param name="minimumCharacterCount">The length a chunk must reach before a separator may end it.</param>
    /// <param name="overlapCharacterCount">How far back into the previous chunk the next one starts.</param>
    /// <param name="boundarySeparators">The separators to break after, from the strongest to the weakest.</param>
    /// <param name="sourceForm">Which reading of the message body to cut from.</param>
    /// <returns>The rule set.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="boundarySeparators" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a separator is empty, which would end every chunk where it began.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a count is not positive, or when the minimum or the overlap is not smaller than the target.</exception>
    public static EmailChunkingRules Create(
        int ruleSetVersion,
        int targetCharacterCount,
        int minimumCharacterCount,
        int overlapCharacterCount,
        IReadOnlyList<string> boundarySeparators,
        EmailChunkSourceForm sourceForm)
    {
        ArgumentNullException.ThrowIfNull(boundarySeparators);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ruleSetVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetCharacterCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumCharacterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapCharacterCount);

        // Both bounds are what keeps the cut moving forward: an overlap that reached the target would restart every
        // chunk where the previous one began, and a minimum at the target would refuse every separator break there is.
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minimumCharacterCount, targetCharacterCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapCharacterCount, targetCharacterCount);

        if (boundarySeparators.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("A boundary separator must not be empty.", nameof(boundarySeparators));
        }

        return new EmailChunkingRules(
            ruleSetVersion,
            targetCharacterCount,
            minimumCharacterCount,
            overlapCharacterCount,
            [.. boundarySeparators],
            sourceForm);
    }
}
