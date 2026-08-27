// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Folders;

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>The words the mailbox tree is written in, and the one place their entries are named.</summary>
/// <remarks>
/// A tree row is composed from what the deployment answered rather than fixed per control, so its sentences are asked
/// for from code instead of through a <c>x:Uid</c> — which makes a name written here the single way a reader would meet
/// a resource key on the screen instead of a sentence. The unit suite holds every authored table to answering all of
/// them.
/// </remarks>
internal static class MailboxWords
{
    /// <summary>The entry naming every mailbox at once.</summary>
    internal const string EverythingKey = "Mailboxes.Everything";

    /// <summary>The entry a role taken across every mailbox is named through, which takes the role's own name.</summary>
    internal const string UnifiedRoleKey = "Mailboxes.Unified";

    private const string StandingKeyPrefix = "Mailboxes.Standing.";
    private const string FreshnessKeyPrefix = "Mailboxes.Freshness.";
    private const string RoleKeyPrefix = "Mailboxes.Role.";

    /// <summary>
    /// The special-use roles the tree offers, in the order a person expects to read them rather than in the order the
    /// deployment publishes them.
    /// </summary>
    /// <remarks>
    /// Stated rather than derived from the enum's own values, because the order a mailbox is read in is a product
    /// decision and the order the values were allocated in is a compatibility one — the second may never be rearranged
    /// to serve the first. The unit suite holds this list to naming every role the client knows, so a role added
    /// without a place here is reported rather than quietly dropped from every tree.
    /// </remarks>
    internal static ImmutableArray<MailFolderRole> RolesInReadingOrder { get; } =
    [
        MailFolderRole.Inbox,
        MailFolderRole.Drafts,
        MailFolderRole.Sent,
        MailFolderRole.Outbox,
        MailFolderRole.Archive,
        MailFolderRole.Junk,
        MailFolderRole.Trash,
        MailFolderRole.All,
        MailFolderRole.Flagged,
        MailFolderRole.Important,
    ];

    /// <summary>Names the entry a standing's sentence is authored under.</summary>
    /// <param name="standing">The standing to name.</param>
    /// <returns>The resource key.</returns>
    internal static string StandingResourceKeyFor(MailSynchronizationStanding standing) =>
        $"{StandingKeyPrefix}{standing}";

    /// <summary>Names the entry a freshness band's sentence is authored under.</summary>
    /// <param name="gap">The band to name.</param>
    /// <returns>The resource key.</returns>
    internal static string FreshnessResourceKeyFor(FreshnessGap gap) => $"{FreshnessKeyPrefix}{gap}";

    /// <summary>Whether a published role name is one the tree offers across mailboxes.</summary>
    /// <param name="published">The name to judge, which may be <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the tree knows the role and would draw a row for it.</returns>
    /// <remarks>
    /// What a remembered scope is judged against before it is restored. A name kept by a build that offered a role
    /// this one does not is forgotten rather than fatal, on the same terms as a deployment address that no longer
    /// passes the rule — nobody wrote it, so nobody can go and correct it.
    /// </remarks>
    internal static bool IsOfferedRole(string? published) =>
        published is not null
        && RolesInReadingOrder.Any(role => string.Equals(role.ToString(), published, StringComparison.Ordinal));

    /// <summary>Names the entry a special-use role's own name is authored under.</summary>
    /// <param name="role">The role to name.</param>
    /// <returns>The resource key.</returns>
    internal static string RoleResourceKeyFor(MailFolderRole role) => RoleResourceKeyFor(role.ToString());

    /// <summary>Names the entry a special-use role's own name is authored under, from the name the deployment published.</summary>
    /// <param name="published">The role's published name, which is what a scope carries.</param>
    /// <returns>The resource key.</returns>
    /// <remarks>
    /// A scope names a role by the word the deployment sent rather than by a value of this client's, because the scope
    /// is what a request will carry. Only a name <see cref="IsOfferedRole" /> admits ever reaches here, so the key this
    /// composes is always one an authored table answers.
    /// </remarks>
    internal static string RoleResourceKeyFor(string published) => $"{RoleKeyPrefix}{published}";

    /// <summary>Says how long ago a copy last took anything in, in the band a person decides on.</summary>
    /// <param name="lastSynchronizedAt">When it last durably took mail in, or <see langword="null" /> where it never has.</param>
    /// <param name="now">When the gap is measured from.</param>
    /// <returns>The band the gap falls in.</returns>
    /// <remarks>
    /// A timestamp ahead of <paramref name="now" /> reads as the narrowest band rather than as a negative gap. The two
    /// clocks are a person's device and a deployment somewhere else, so a few seconds of disagreement between them is
    /// ordinary and is not something to put on a screen.
    /// </remarks>
    internal static FreshnessGap GapAt(DateTimeOffset? lastSynchronizedAt, DateTimeOffset now) =>
        lastSynchronizedAt is not { } taken
            ? FreshnessGap.Never
            : (now - taken) switch
            {
                { TotalHours: < 1 } => FreshnessGap.WithinTheHour,
                { TotalDays: < 1 } => FreshnessGap.Today,
                { TotalDays: < 7 } => FreshnessGap.WithinTheWeek,
                _ => FreshnessGap.LongerAgo,
            };
}
