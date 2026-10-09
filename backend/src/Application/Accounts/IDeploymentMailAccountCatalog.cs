// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Describes every mail account this deployment serves, whoever owns it.</summary>
/// <remarks>
/// <para>
/// This is the deployment's own answer and it belongs to work the deployment does for itself: the synchronization
/// coordinator that starts a run per account, the schedule that evaluates rules against each of them, the status an
/// operator reads, and the administrative operations an operator performs on an account they name. None of those acts
/// for one user, and each of them would be wrong if it saw one user's half of the deployment.
/// </para>
/// <para>
/// It is deliberately not what a caller-facing use case reads. A read that answers a person about their own mail asks
/// <see cref="ICallerMailAccountCatalog" /> instead, and the two are separate ports with differently named members so a
/// read model that reaches for the wrong one names the wrong member rather than compiling and answering across users.
/// The rule is on the operation rather than on the surface: an administrative operation reaches this one, and an
/// operation a person performs about their own accounts reaches the other, and an operation that is both is two
/// operations.
/// </para>
/// <para>
/// An account nobody configured is refused rather than answered with an empty page, because at a boundary a client
/// reaches, an empty page tells the client the name exists and holds no mail, which turns a list operation into a way to
/// enumerate accounts.
/// </para>
/// </remarks>
public interface IDeploymentMailAccountCatalog
{
    /// <summary>Gets whether this deployment refreshes the local copy of these accounts at all.</summary>
    /// <remarks>
    /// It reports the operator's synchronization switch, which is a fact about the deployment rather than about any one
    /// account. Nothing gates account membership on it: a deployment that switched synchronization off still serves the
    /// mail it already stored, and a reader that saw only the accounts would have no way to tell a mailbox that is
    /// merely quiet from one nothing is updating.
    /// </remarks>
    bool SynchronizationEnabled { get; }

    /// <summary>Reads every account this deployment serves from the account records, deduplicated and ordered.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The accounts in the ordinal order of their identifiers, so a scope resolved from this set is canonical and a
    /// continuation cursor issued for it stays valid while the records do not change. An empty set means no account is
    /// served and therefore that no stored mail is readable, which is a state configuration allows: an operator may switch
    /// synchronization off and remove every account while a local copy still exists.
    /// </returns>
    /// <remarks>
    /// The shape a reader of the whole set takes — the status an operator reads, the schedule that evaluates rules
    /// against every account — because it answers from the records themselves rather than from what this process
    /// composed, so a deployment's account count is a question of how large the answer is rather than of what every
    /// replica must hold.
    /// </remarks>
    Task<IReadOnlyList<ServedMailAccount>> ReadServedAccountsAsync(CancellationToken cancellationToken);

    /// <summary>Reads which of the named accounts this deployment serves.</summary>
    /// <param name="among">The accounts asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The served accounts among those named, in the ordinal order of their identifiers; one the deployment does not serve is left out.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="among" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The shape a reader that has an account in hand takes — an operator naming one, a credential recorded against one,
    /// a caller's own assigned accounts — because it reads those accounts rather than the whole set, so what it costs is
    /// the size of the question rather than the size of the deployment.
    /// </remarks>
    Task<IReadOnlyList<ServedMailAccount>> ReadServedAccountsAsync(
        IReadOnlyCollection<MailAccountId> among,
        CancellationToken cancellationToken);
}
