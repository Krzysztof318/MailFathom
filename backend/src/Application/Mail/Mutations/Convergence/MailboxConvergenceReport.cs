// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Mutations.Convergence;

/// <summary>States what one convergence pass did and what the account is left owing.</summary>
/// <param name="CompletedCount">How many mutations this pass carried to the stage that says the server made the change.</param>
/// <param name="DeadLetteredCount">How many it gave up on, each of which now stands visible rather than pending.</param>
/// <param name="DeferredCount">How many it left where they were, to be picked up by a later pass or settled by observation.</param>
/// <param name="FailedCount">How many ended in a failure that the mutation's own attempt bound will eventually settle.</param>
/// <param name="Outstanding">What the account still owes after the pass, by kind and lifecycle.</param>
/// <remarks>
/// <para>
/// The four counts describe the pass and <paramref name="Outstanding" /> describes the account, which are different
/// questions and are read by different people. A pass that did nothing is ordinary — most passes have nothing to do —
/// while an account whose dead-lettered count keeps growing is the finding this whole mechanism exists to surface.
/// </para>
/// <para>
/// Nothing here is derived from a message. Counts, mutation names, and lifecycles are MailFathom's own words for its
/// own work.
/// </para>
/// </remarks>
public sealed record MailboxConvergenceReport(
    int CompletedCount,
    int DeadLetteredCount,
    int DeferredCount,
    int FailedCount,
    IReadOnlyList<MailboxMutationLifecycleCount> Outstanding)
{
    /// <summary>Gets whether the pass changed nothing, which is what an account with no outstanding work produces.</summary>
    public bool ChangedNothing =>
        this.CompletedCount == 0 && this.DeadLetteredCount == 0 && this.FailedCount == 0;
}
