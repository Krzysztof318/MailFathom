// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Removes the objects a committed unit of work stopped pointing at.</summary>
/// <remarks>
/// <para>
/// This is the half of erasure the database gets for free. <c>DeleteBehavior.Cascade</c> takes a payload row with the
/// message it belongs to, inside one transaction, and a rolled-back deletion leaves nothing behind — a bucket offers
/// none of that. So a deletion path collects the locators inside its transaction, before the rows go, and this runs
/// once that transaction has committed.
/// </para>
/// <para>
/// <b>After the commit, never before it.</b> Deleting an object first would destroy mail whose deletion then rolled
/// back, which is irreversible loss on a transient failure; deleting it afterwards can leave an object behind, which is
/// an orphan the sweep removes.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see>
/// § 7 records that choice.
/// </para>
/// <para>
/// Nothing here raises. The row is already gone and the caller's write already succeeded, so a failure to remove the
/// object is recorded and left to reclamation rather than reported as a write that did not happen. That is what makes
/// the sweep the guarantee rather than a convenience.
/// </para>
/// <para>
/// A deployment storing content in the database registers no endpoint, so this is handed no object store and has
/// nothing to do. So is one that stored mail through an endpoint and then lost the configuration — there the locators
/// are left where they are, which the readiness probe already reports as the unhealthy deployment it is.
/// </para>
/// </remarks>
internal sealed partial class ReleasedContentObjectEraser(
    ContentObjectReclamationTelemetry telemetry,
    ILogger<ReleasedContentObjectEraser> logger,
    IEmailContentObjectStore? objectStore = null)
{
    /// <summary>Removes every object a committed unit of work released, and records what would not go.</summary>
    /// <param name="objectLocators">The whole keys the deleted rows carried.</param>
    /// <param name="cancellationToken">Cancels the removals, leaving what is left to reclamation.</param>
    /// <returns>A task that completes once every locator has been attempted or the caller cancelled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="objectLocators" /> is <see langword="null" />.</exception>
    public async Task EraseAsync(IReadOnlyCollection<string> objectLocators, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(objectLocators);

        if (objectStore is null || objectLocators.Count == 0)
        {
            return;
        }

        var erasedCount = 0;
        var failedCount = 0;

        foreach (var objectLocator in objectLocators)
        {
            // Cancellation stops the loop rather than ending the method on an exception, so what was already removed is
            // still recorded and the remainder is an orphan the sweep meets on its next pass.
            if (cancellationToken.IsCancellationRequested)
            {
                failedCount += objectLocators.Count - erasedCount - failedCount;

                break;
            }

            try
            {
                await objectStore.DeleteAsync(objectLocator, cancellationToken);
                erasedCount++;
            }
            catch (ObjectStorageUnavailableException unavailable)
            {
                failedCount++;
                this.LogObjectNotRemoved(unavailable.Failure.Name, unavailable);
            }
            catch (OperationCanceledException)
            {
                failedCount++;
            }
        }

        telemetry.RecordErased(erasedCount, failedCount);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The object-storage endpoint did not remove a payload whose row a committed erasure deleted ({Failure}); the object holds mail nothing points at and is reclaimed by the next sweep that meets it.")]
    private partial void LogObjectNotRemoved(string failure, Exception cause);
}
