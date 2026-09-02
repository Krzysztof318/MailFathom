// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam;

/// <summary>Which of the deployment's mailboxes classification runs for, and over which of their folders.</summary>
/// <remarks>
/// <para>
/// The whole deployment's answer, composed from every owner's own posture and read once. Classification is each owner's
/// decision about their own mail, so a walk over stored mail cannot ask one question of the deployment — it has to know
/// which accounts belong to an owner who classifies and which folders of those accounts are in that owner's scope.
/// </para>
/// <para>
/// Both are named by account rather than by owner because an account belongs to exactly one owner and the tables a walk
/// narrows already carry the account, which keeps the decision one more term of an existing predicate rather than a
/// join onto the owner.
/// </para>
/// <para>
/// The wait is here rather than being one owner's, because how long the index may be held back by a scanner that has
/// stopped answering is a cost the process bears: an owner who could raise it would be raising it for work that is not
/// theirs.
/// </para>
/// </remarks>
public sealed record SpamClassificationScope
{
    /// <summary>The wait a verdict is allowed when an operator names none.</summary>
    /// <remarks>
    /// Long enough that an ordinary classification backlog drains inside it and short enough that a wedged scanner
    /// delays the index by a visible amount rather than an unbounded one. Nothing is lost by the release: a message let
    /// through unclassified is chunked and embedded like any other, and a verdict that arrives afterwards is what a
    /// later reading of the gate acts on.
    /// </remarks>
    public static readonly TimeSpan DefaultMaximumClassificationWait = TimeSpan.FromMinutes(15);

    private SpamClassificationScope(
        IReadOnlyList<MailAccountId> classifyingAccounts,
        IReadOnlyList<MailFolderIdentity> classifiedFolders,
        TimeSpan maximumClassificationWait)
    {
        this.ClassifyingAccounts = classifyingAccounts;
        this.ClassifiedFolders = classifiedFolders;
        this.MaximumClassificationWait = maximumClassificationWait;
    }

    /// <summary>Gets the scope of a deployment no owner of which classifies anything.</summary>
    public static SpamClassificationScope None { get; } = new([], [], DefaultMaximumClassificationWait);

    /// <summary>Gets the accounts whose owner has classification switched on, in a normalized order.</summary>
    /// <remarks>
    /// Empty for a deployment nobody classifies for, which is what makes every path that obeys the gate behave exactly
    /// as it did before the gate existed. An account absent from it is one no verdict is expected for, whether its owner
    /// switched classification off or never switched it on.
    /// </remarks>
    public IReadOnlyList<MailAccountId> ClassifyingAccounts { get; }

    /// <summary>Gets the folders classification runs over, each named within the account that holds it.</summary>
    /// <remarks>
    /// Named as a pair rather than as an alias, because two owners may map the same alias while only one of them
    /// classifies: narrowing by the alias alone would hold the other owner's mail back for a verdict nothing is going to
    /// reach about it. Every entry belongs to an account of <see cref="ClassifyingAccounts" />.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> ClassifiedFolders { get; }

    /// <summary>Gets how long a message may wait on a verdict before derived work runs for it unclassified.</summary>
    /// <remarks>
    /// The bound that keeps ordering classification ahead of chunking, embedding, and rule evaluation from turning a
    /// wedged scanner into an index that quietly stops filling. A message still inside it is waiting rather than
    /// failing, which is a distinction the gate has to make: a rule that released only what had failed would never
    /// release anything sitting in a backlog, and a backlog deep enough to matter is exactly where that decides whether
    /// the index stops.
    /// </remarks>
    public TimeSpan MaximumClassificationWait { get; }

    /// <summary>Composes the deployment's scope from the owners that classify and the folders they classify over.</summary>
    /// <param name="classifyingAccounts">The accounts of every owner whose classification is switched on.</param>
    /// <param name="classifiedFolders">The folders those accounts are classified over.</param>
    /// <param name="maximumClassificationWait">The deployment's wait, or <see langword="null" /> for the default.</param>
    /// <returns>The scope, with both lists deduplicated and ordered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either sequence is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the wait is not positive.</exception>
    public static SpamClassificationScope Create(
        IEnumerable<MailAccountId> classifyingAccounts,
        IEnumerable<MailFolderIdentity> classifiedFolders,
        TimeSpan? maximumClassificationWait = null)
    {
        ArgumentNullException.ThrowIfNull(classifyingAccounts);
        ArgumentNullException.ThrowIfNull(classifiedFolders);

        var wait = maximumClassificationWait ?? DefaultMaximumClassificationWait;

        if (wait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumClassificationWait),
                wait,
                "A message waits a positive length of time on a verdict, because a wait of none releases every message before anything could classify it.");
        }

        return new SpamClassificationScope(
            [
                .. classifyingAccounts
                    .DistinctBy(static account => account.Value, StringComparer.Ordinal)
                    .OrderBy(static account => account.Value, StringComparer.Ordinal),
            ],
            [
                .. classifiedFolders
                    .Distinct()
                    .OrderBy(static folder => folder.AccountId.Value, StringComparer.Ordinal)
                    .ThenBy(static folder => folder.Alias.Value, StringComparer.Ordinal),
            ],
            wait);
    }
}
