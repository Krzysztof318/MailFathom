// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Offers what a message asked for to every person the mailbox it arrived in is assigned to.</summary>
/// <remarks>
/// <para>
/// The seam between the enrichment pass and the task list, and the only producer of a
/// <see cref="PersonalTaskOrigin.Proposed" /> task. It exists as a use case of its own rather than as a few lines
/// inside the pass because the two answer different questions: the pass decides which messages are derived from and
/// what one derivation costs, and this decides whose list a reading lands on and under which origin.
/// </para>
/// <para>
/// <b>A proposal is never a commitment.</b> Everything written here carries the origin that says nobody has agreed to
/// it, so it stands on the half of the list a person accepts or dismisses from. Accepting is their act, through the
/// route that owns it, and nothing in an unattended pass can reach the other half.
/// </para>
/// <para>
/// <b>It is written per person rather than per mailbox</b>, because a task list is one person's. A mailbox two people
/// are assigned produces one derivation — the mail is one copy and so is everything derived from it — and each of them
/// is offered the same thing on their own list, which each may accept or dismiss without deciding anything for the
/// other. A mailbox assigned to nobody proposes to nobody rather than to everybody.
/// </para>
/// <para>
/// <b>The message is cited and never copied.</b> What the task keeps is the identity of the message it was read out
/// of, so the thread can be opened from the list; no subject, no body, and no address is written beside it, and the
/// task outlives the citation because what somebody owes does not stop being owed when the mail naming it is erased.
/// </para>
/// </remarks>
public sealed class MailDerivedTaskProposals
{
    private readonly IMailAccountAssignments assignments;
    private readonly IPersonalTaskStore store;
    private readonly TimeProvider clock;

    /// <summary>Initializes the use case.</summary>
    /// <param name="assignments">Answers which people the mailbox a message arrived in is assigned to.</param>
    /// <param name="store">Keeps what each of them owes and what has been proposed to them.</param>
    /// <param name="clock">Stamps the identity each proposed task is addressed under.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailDerivedTaskProposals(
        IMailAccountAssignments assignments,
        IPersonalTaskStore store,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        this.assignments = assignments;
        this.store = store;
        this.clock = clock;
    }

    /// <summary>Writes one message's readings onto the list of each person assigned the mailbox it arrived in.</summary>
    /// <param name="account">The mailbox the message arrived in, which is what decides whose lists these reach.</param>
    /// <param name="message">The message the readings came out of, which every task written here cites.</param>
    /// <param name="proposals">What the message asked for, which is empty where it asked for nothing.</param>
    /// <param name="cancellationToken">Cancels the writes; tasks already written stay durable.</param>
    /// <returns>How many tasks were written, over every person and every reading.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="proposals" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// Nothing is read before anything is written, and a task already proposed from this message is not looked for.
    /// The pass takes a message out of the selection by deriving from it, so one message produces one derivation and
    /// the duplicate this would guard against has no path to occur — and a read per person per message would be a
    /// query on the arrival path for a case the pass already rules out.
    /// </remarks>
    public async Task<int> ProposeAsync(
        MailAccountId account,
        StoredEmailId message,
        IReadOnlyList<EmailTaskProposal> proposals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        if (proposals.Count is 0)
        {
            return 0;
        }

        var written = 0;

        foreach (var user in this.assignments.UsersAssignedTo(account))
        {
            foreach (var proposal in proposals)
            {
                var task = PersonalTask.Compose(
                    PersonalTaskId.Create(Guid.CreateVersion7(this.clock.GetUtcNow())),
                    user,
                    proposal.Title,
                    proposal.DueOn,
                    PersonalTaskOrigin.Proposed,
                    message);

                await this.store.AddAsync(task, cancellationToken);

                written++;
            }
        }

        return written;
    }
}
