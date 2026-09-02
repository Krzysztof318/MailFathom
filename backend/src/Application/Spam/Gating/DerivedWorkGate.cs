// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Gating;

/// <summary>Decides whether the work derived from a message may run yet, from where the message is and what was decided about it.</summary>
/// <remarks>
/// <para>
/// Nothing expensive happens to a message before it is known not to be junk, and nothing expensive happens to it at all
/// if it is. That is the whole of the mechanism: chunking, embedding, and rule evaluation are ordered behind
/// classification rather than compensated for afterwards, so a message on its way to the junk folder is never chunked,
/// never embedded, and never offered to a rule — the work was not cancelled, it was never started.
/// </para>
/// <para>
/// The one failure mode it must not have is turning a wedged scanner into a silently dead index. A message that cannot
/// be classified, and one that has waited longer than a verdict is allowed to take, are both released to derived work
/// and counted as released; only a message genuinely still waiting is held, and only for as long as the wait permits.
/// </para>
/// <para>
/// It reads and never writes. No flag records that a message was withheld, which is what makes mail the owner drags out
/// of the junk folder ordinary mail from that moment: the next reading of the same four facts admits it, and the
/// ordinary backfill picks it up.
/// </para>
/// <para>
/// Whether it reaches a message at all is that message's owner's decision. The terms name the accounts of the owners
/// who classify, so an owner who switched classification off has every one of their messages admitted while another
/// owner's mail goes on waiting on its verdict — and a walk that spans both applies each answer to the mail it is about.
/// </para>
/// </remarks>
public sealed class DerivedWorkGate
{
    private readonly ISpamClassificationSettingsReader settingsReader;
    private readonly IJunkMailFolderCatalog junkFolders;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the gate from the decisions it obeys.</summary>
    /// <param name="settingsReader">Answers which owners classify, over which of their folders, and how long a verdict may take.</param>
    /// <param name="junkFolders">Answers which folder of an account its server files junk into.</param>
    /// <param name="timeProvider">Reads the moment a wait is measured against.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DerivedWorkGate(
        ISpamClassificationSettingsReader settingsReader,
        IJunkMailFolderCatalog junkFolders,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(junkFolders);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.settingsReader = settingsReader;
        this.junkFolders = junkFolders;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads the terms in force now, as one snapshot a whole walk is decided under.</summary>
    /// <returns>The terms, which admit everything belonging to an owner who classifies nothing.</returns>
    /// <remarks>
    /// The junk folders are narrowed to the accounts of owners who classify, which is what keeps the withholding an
    /// ordering behind classification rather than a rule of its own: an owner who switched classification off has
    /// mail in their junk folder derived from like any other, exactly as every owner did before the gate existed.
    /// </remarks>
    public DerivedWorkAdmissionTerms ReadTerms()
    {
        var scope = this.settingsReader.ScopeInForce;

        return new DerivedWorkAdmissionTerms(
            scope.ClassifyingAccounts,
            [.. this.junkFolders.JunkFolders.Where(folder => scope.ClassifyingAccounts.Contains(folder.AccountId))],
            scope.ClassifiedFolders,
            this.timeProvider.GetUtcNow() - scope.MaximumClassificationWait);
    }

    /// <summary>Decides what classification says about one occurrence right now.</summary>
    /// <param name="candidate">The four facts an admission is decided from.</param>
    /// <returns>The admission, which only <see cref="DerivedWorkAdmission.Admitted" /> and the two released answers permit derived work.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    public DerivedWorkAdmission Admit(DerivedWorkCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return Admit(this.ReadTerms(), candidate);
    }

    /// <summary>Decides what one occurrence's admission is under terms already read.</summary>
    /// <param name="terms">The snapshot the decision is made under.</param>
    /// <param name="candidate">The four facts an admission is decided from.</param>
    /// <returns>The admission.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The order of the questions is the order of what each one settles. Placement is asked before the record, because
    /// mail already sitting in the junk folder is junk with nothing having scored it and a reversal has to be able to
    /// undo a verdict that scoring reached. Scope is asked before the wait, because a folder no classification runs over
    /// is one whose mail would otherwise wait for a verdict nothing is going to produce.
    /// </remarks>
    public static DerivedWorkAdmission Admit(DerivedWorkAdmissionTerms terms, DerivedWorkCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!terms.IsAppliedFor(candidate.AccountId))
        {
            return DerivedWorkAdmission.Admitted;
        }

        if (IsJunkFolder(terms, candidate))
        {
            return DerivedWorkAdmission.WithheldAsJunk;
        }

        if (candidate.Verdict is { } verdict)
        {
            return verdict is SpamVerdict.Spam
                ? DerivedWorkAdmission.WithheldAsJunk
                : DerivedWorkAdmission.Admitted;
        }

        if (!terms.Classifies(candidate.AccountId, candidate.FolderAlias))
        {
            return DerivedWorkAdmission.Admitted;
        }

        if (candidate.ContentAvailability is StoredEmailContentAvailability.ExceededSizeLimit)
        {
            return DerivedWorkAdmission.ReleasedAsUnclassifiable;
        }

        return candidate.StoredAt <= terms.ReleasedWhenStoredBefore
            ? DerivedWorkAdmission.ReleasedAfterWaiting
            : DerivedWorkAdmission.AwaitingClassification;
    }

    private static bool IsJunkFolder(DerivedWorkAdmissionTerms terms, DerivedWorkCandidate candidate) =>
        terms.JunkFolders.Any(folder =>
            folder.AccountId == candidate.AccountId && folder.Alias == candidate.FolderAlias);
}
