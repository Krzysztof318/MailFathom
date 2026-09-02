// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Does the work one job type describes, and knows nothing about how that work was leased.</summary>
/// <remarks>
/// <para>
/// A handler receives a payload and a cancellation token. Claiming the job, renewing its lease while this runs, and
/// recording what happened when it stops all belong to the worker, which is what lets a consumer be written and tested
/// without a database and what keeps persistence out of application work.
/// </para>
/// <para>
/// Failure is raised rather than returned. Nothing here decides whether a failure is worth repeating, so a handler that
/// classified its own would be answering a question this contract cannot ask; what it does instead is let the exception
/// travel, and the worker records the attempt against the job.
/// </para>
/// <para>
/// The token is cancelled when the execution exceeds its configured timeout, when the host is stopping, and when the
/// lease is lost to another attempt. A handler that ignores it is a handler the worker cannot stop, so long work
/// observes it between steps and abandons what it has not committed.
/// </para>
/// <para>
/// Execution is at least once. A crash between the work and its recorded outcome leaves the job claimable again, so a
/// handler is registered on the promise that running it twice with one payload is the same as running it once.
/// </para>
/// </remarks>
public interface IJobHandler
{
    /// <summary>Gets the job type this handler runs, which is also the contract its payload arrives under.</summary>
    JobType JobType { get; }

    /// <summary>Runs the work the payload describes.</summary>
    /// <param name="payload">The references the work is described by, of the contract <see cref="JobType" /> names.</param>
    /// <param name="cancellationToken">Cancels the work at the execution timeout, at host shutdown, and when the lease is lost.</param>
    /// <returns>A task that completes when the work is done.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the work observes cancellation and stops.</exception>
    Task RunAsync(IJobPayload payload, CancellationToken cancellationToken);
}
