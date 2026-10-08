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
/// It reads and never writes. No flag records that a message was withheld, which is what makes mail the user drags out
/// of the junk folder ordinary mail from that moment: the next reading of the same four facts admits it, and the
/// ordinary backfill picks it up.
/// </para>
/// <para>
/// Whether it reaches a message at all is that message's user's decision. The terms name the accounts of the users
/// who classify, so a user who switched classification off has every one of their messages admitted while another
/// user's mail goes on waiting on its verdict — and a walk that spans both applies each answer to the mail it is about.
/// </para>
/// </remarks>
public sealed class DerivedWorkGate
{
    private readonly ISpamClassificationSettingsReader settingsReader;
    private readonly IDeploymentMailFolders deploymentFolders;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the gate from the decisions it obeys.</summary>
    /// <param name="settingsReader">Answers which users classify, over which of their folders, and how long a verdict may take.</param>
    /// <param name="deploymentFolders">Answers which folder of each account its server files junk into.</param>
    /// <param name="timeProvider">Reads the moment a wait is measured against.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DerivedWorkGate(
        ISpamClassificationSettingsReader settingsReader,
        IDeploymentMailFolders deploymentFolders,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(deploymentFolders);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.settingsReader = settingsReader;
        this.deploymentFolders = deploymentFolders;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads the terms in force now, as one snapshot a whole walk is decided under.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The terms, which admit everything belonging to a user who classifies nothing.</returns>
    /// <remarks>
    /// <para>
    /// The junk folders are narrowed to the accounts of users who classify, which is what keeps the withholding an
    /// ordering behind classification rather than a rule of its own: a user who switched classification off has
    /// mail in their junk folder derived from like any other, exactly as every user did before the gate existed.
    /// </para>
    /// <para>
    /// Every part of it is read from the account records, so a caller reads it once for its work rather than once per
    /// message: a walk reads it per batch, and a run that meets messages one at a time reads it once for the run and
    /// decides each of them with <see cref="Admit(DerivedWorkAdmissionTerms, DerivedWorkCandidate)" />.
    /// </para>
    /// </remarks>
    public async Task<DerivedWorkAdmissionTerms> ReadTermsAsync(CancellationToken cancellationToken)
    {
        var scope = await this.settingsReader.ReadScopeInForceAsync(cancellationToken);
        var junkFolders = await this.deploymentFolders.ReadAsync(MailFolderSelection.Junk, cancellationToken);

        return new DerivedWorkAdmissionTerms(
            scope.ClassifyingAccounts,
            [.. junkFolders.Where(folder => scope.ClassifyingAccounts.Contains(folder.AccountId))],
            scope.ClassifiedFolders,
            this.timeProvider.GetUtcNow() - scope.MaximumClassificationWait);
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
