// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Coordination;

/// <summary>Runs one piece of work while this replica holds the lease on its scope, and runs nothing while another does.</summary>
/// <remarks>
/// <para>
/// The seam application work takes a lease through, because taking one is more than a claim: the lease has to be renewed
/// while the work runs, each statement against the store needs a scope of its own so it never shares a persistence
/// session with the work it guards, and the lease has to be given back once the work ends. All three are the host's, so
/// the work is handed in rather than the lease handed out, and a caller cannot forget the release.
/// </para>
/// <para>
/// What a caller is promised is <see cref="WorkLease" />'s: one writer, never one runner. The token the work receives is
/// cancelled on the first renewal that does not complete, strictly before the lease could expire and another replica
/// take the scope, so work that observes it between steps commits nothing as a second writer.
/// </para>
/// </remarks>
public interface IWorkLeaseRunner
{
    /// <summary>Takes the lease on a scope, runs the work under it, and gives the lease back once the work ends.</summary>
    /// <param name="scope">The unit of work to hold.</param>
    /// <param name="work">The work, handed a token cancelled with <paramref name="cancellationToken" /> and when the lease is lost.</param>
    /// <param name="cancellationToken">Cancels the claim and the work.</param>
    /// <returns><see langword="true" /> when the work ran under the lease; <see langword="false" /> when another holder has the scope or the claim could not be made, in which case the work never started.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled while the lease is asked for.</exception>
    /// <remarks>
    /// A refusal is not a failure. The work belongs to another holder for now, or the database could not be asked, and in
    /// both cases the caller asks again on the interval it would have run on.
    /// </remarks>
    Task<bool> TryRunUnderLeaseAsync(
        WorkScope scope,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken);
}
