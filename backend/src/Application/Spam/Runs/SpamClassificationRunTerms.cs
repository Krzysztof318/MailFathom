// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam.Runs;

/// <summary>What an operator asked a whole-mailbox classification run to do.</summary>
/// <remarks>
/// <para>
/// The three answers a run needs beyond the account, and all three are fixed when it is asked for rather than read again
/// while it walks. A run that spans hours of account runs must mean the same thing at its end as at its start, so an
/// operator who edits configuration mid-walk changes the next run and not this one.
/// </para>
/// <para>
/// Every default is the cautious one: the scope the deployment already classifies, no rescoring of what has been decided
/// under the terms in force, and a dry run.
/// </para>
/// </remarks>
public sealed record SpamClassificationRunTerms
{
    private SpamClassificationRunTerms(
        IReadOnlyList<MailFolderAlias> folderAliases,
        SpamActionPosture posture,
        bool rescores)
    {
        this.FolderAliases = folderAliases;
        this.Posture = posture;
        this.Rescores = rescores;
    }

    /// <summary>Gets the folders the run walks, in a normalized order.</summary>
    /// <remarks>
    /// Named rather than left implicit, because the run outlives the configuration it was asked under: reading the scope
    /// again on each pass would let a folder added halfway through a mailbox be walked from wherever the run had got to
    /// rather than from its beginning, and a reader of the record could not say which mail the run had covered.
    /// </remarks>
    public IReadOnlyList<MailFolderAlias> FolderAliases { get; }

    /// <summary>Gets whether the run writes down what its verdicts ask of the mailbox, or only works it out.</summary>
    public SpamActionPosture Posture { get; }

    /// <summary>Gets whether mail already classified under the run's profile is scored again rather than skipped.</summary>
    /// <remarks>
    /// Off, the run passes over a message whose record already names the terms in force and still acts on that record,
    /// which is what makes switching filing on a run that files rather than a run that re-reads a mailbox. On, every
    /// message in scope is read and scored afresh — the explicit request that re-scoring is, and the one form of this run
    /// that costs a scanner call per message however recently the message was decided.
    /// </remarks>
    public bool Rescores { get; }

    /// <summary>Builds the terms an operator's answers describe.</summary>
    /// <param name="folderAliases">The folders to walk, already resolved to the configured scope where the operator named none.</param>
    /// <param name="posture">Whether the run acts on the mailbox.</param>
    /// <param name="rescores">Whether mail already decided under the run's profile is scored again.</param>
    /// <returns>The terms, with the alias list deduplicated and ordered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folderAliases" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="posture" /> is not a defined member.</exception>
    public static SpamClassificationRunTerms Create(
        IEnumerable<MailFolderAlias> folderAliases,
        SpamActionPosture posture,
        bool rescores)
    {
        ArgumentNullException.ThrowIfNull(folderAliases);

        if (!Enum.IsDefined(posture))
        {
            throw new ArgumentOutOfRangeException(
                nameof(posture),
                posture,
                "A run either writes the changes its verdicts ask for down or works them out and writes nothing.");
        }

        return new SpamClassificationRunTerms(
            [
                .. folderAliases
                    .DistinctBy(static alias => alias.Value, StringComparer.Ordinal)
                    .OrderBy(static alias => alias.Value, StringComparer.Ordinal),
            ],
            posture,
            rescores);
    }
}
