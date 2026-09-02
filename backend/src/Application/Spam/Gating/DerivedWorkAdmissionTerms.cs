// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam.Gating;

/// <summary>The terms one moment's admissions are decided under, as a value a query can be narrowed by.</summary>
/// <remarks>
/// <para>
/// The reason this exists beside <see cref="DerivedWorkGate.Admit(DerivedWorkCandidate)" /> is the reason
/// <see cref="Folders.IMailFolderParticipationReader" /> answers in two shapes: a walk over stored mail narrows a table
/// and needs the whole decision as a value it can put into a predicate, while the arrival path holds one occurrence and
/// asks about that one. Both are built here, from one reading of the settings and one reading of the clock, so the two
/// cannot disagree about what a moment admits.
/// </para>
/// <para>
/// It is a snapshot rather than a live view, so one decision is never made against a settings reload half way through
/// itself. Which moment a walk's batches are decided at is the walk's own business: the wait only ever moves forward,
/// so a batch read later releases what an earlier one held and never the reverse.
/// </para>
/// <para>
/// Everything here is named by account, which is how one owner's decision reaches a walk that spans owners: an account
/// belongs to exactly one owner, so an account absent from <paramref name="ClassifyingAccounts" /> is one whose owner
/// classifies nothing and whose mail is admitted with no verdict expected about it.
/// </para>
/// </remarks>
/// <param name="ClassifyingAccounts">
/// The accounts whose owner has classification switched on. Empty for a deployment nobody classifies for, which is what
/// makes such a deployment behave exactly as it did before the gate existed.
/// </param>
/// <param name="JunkFolders">
/// The junk folder of each classifying account, whose mail is withheld with nothing having to score it. An account
/// whose owner classifies nothing contributes none, so its junk folder is ordinary mail here.
/// </param>
/// <param name="ClassifiedFolders">
/// The folders classification runs over, each within its own account. Mail outside them is admitted rather than left
/// waiting, because nothing is ever going to reach a verdict about it.
/// </param>
/// <param name="ReleasedWhenStoredBefore">
/// The instant a message must have been stored before to be released without a verdict. It is the moment the terms were
/// read, less the wait a verdict is allowed.
/// </param>
public sealed record DerivedWorkAdmissionTerms(
    IReadOnlyList<MailAccountId> ClassifyingAccounts,
    IReadOnlyList<MailFolderIdentity> JunkFolders,
    IReadOnlyList<MailFolderIdentity> ClassifiedFolders,
    DateTimeOffset ReleasedWhenStoredBefore)
{
    /// <summary>Gets whether the gate reaches anything at all.</summary>
    /// <remarks>
    /// False where no owner classifies, which lets a walk skip the narrowing altogether rather than composing a
    /// predicate that excludes nothing.
    /// </remarks>
    public bool IsApplied => this.ClassifyingAccounts.Count > 0;

    /// <summary>Reports whether the gate reaches one account's mail.</summary>
    /// <param name="accountId">The account the occurrence belongs to.</param>
    /// <returns><see langword="true" /> when that account's owner has classification switched on.</returns>
    public bool IsAppliedFor(MailAccountId accountId) => this.ClassifyingAccounts.Contains(accountId);

    /// <summary>Reports whether classification runs over one folder.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns><see langword="true" /> when a verdict is expected for mail in that folder.</returns>
    public bool Classifies(MailAccountId accountId, MailFolderAlias folderAlias) =>
        this.ClassifiedFolders.Contains(new MailFolderIdentity(accountId, folderAlias));
}
