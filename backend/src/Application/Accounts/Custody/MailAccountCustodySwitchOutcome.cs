// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts.Custody;

/// <summary>What an administrative request to change one account's custody did.</summary>
/// <param name="State">The account's custody as it stands after the request, which is unchanged for a refused one.</param>
/// <param name="Refusals">Why the switch was not accepted, empty for one that was.</param>
/// <remarks>
/// A refusal is a result rather than an exception, because every one of them names something an operator corrects and
/// asks again: a mapping to stop synchronizing, a path to write, or an older replica to finish going away. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </remarks>
public sealed record MailAccountCustodySwitchOutcome(
    MailAccountCustodyState State,
    IReadOnlyList<MailAccountCustodyRefusalDetail> Refusals)
{
    /// <summary>Gets whether the requested custody was written.</summary>
    public bool WasAccepted => this.Refusals.Count == 0;
}

/// <summary>One reason a custody switch was refused, and what it was refused over.</summary>
/// <param name="Reason">Why the switch was refused.</param>
/// <param name="Subject">What it was refused over, which is a folder alias or a replica identity and never anything from a message, or <see langword="null" /> for a refusal that names nothing in particular.</param>
/// <remarks>
/// The subject is absent exactly where the refusal is about the reading rather than about a thing it found: a lease
/// page that filled its bound refuses without being able to say which replica, because the replica it is refusing over
/// may be in the part that was not read.
/// </remarks>
public sealed record MailAccountCustodyRefusalDetail(MailAccountCustodySwitchRefusal Reason, string? Subject)
{
    /// <summary>States the refusal in the sentence an operator reads, wherever it is reported.</summary>
    /// <returns>The sentence, naming what to correct.</returns>
    /// <remarks>
    /// Composed here rather than at each boundary because both the administrative endpoint and the command that calls
    /// it report the same refusal, and two copies of the sentence are two sentences that drift apart.
    /// </remarks>
    public string Describe() => this.Reason switch
    {
        MailAccountCustodySwitchRefusal.SynchronizedVirtualFolder =>
            $"The folder '{this.Subject}' plays a role whose messages are occurrences of other folders, and an account holding its own mailbox cannot synchronize one. Stop synchronizing that mapping and ask again.",
        MailAccountCustodySwitchRefusal.ReplicaOnBuildWithoutTheMode when this.Subject is null =>
            "Too many replicas are holding work at once for their builds to be read, so whether one of them does not know this mode could not be established. Ask again once fewer scopes are held.",
        MailAccountCustodySwitchRefusal.ReplicaOnBuildWithoutTheMode =>
            $"Replica '{this.Subject}' is holding work under a build that does not know this mode, and would read a held account as mirrored. The refusal clears once that replica's lease expires.",
        _ =>
            $"The folder mapping '{this.Subject}' names no folder the source server holds, and does not permit creating one there, so the mailbox could not be appended back. Correct its remote path and ask again.",
    };
}
