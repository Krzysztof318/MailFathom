// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;

namespace MailFathom.Application.StoredFiles;

/// <summary>Removes stored files no record links to, one bounded run at a time, on whichever replica holds the sweep.</summary>
/// <remarks>
/// <para>
/// A file is written before the record links to it, so a failure between the two leaves a file nothing links to. The
/// age floor is what separates that from a write whose link has not committed yet: a file younger than it is never a
/// candidate, whatever its record says.
/// </para>
/// <para>
/// The sweep is one run for the whole deployment, so it runs only under the lease on its scope, as
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// decides. A replica refused the lease removes nothing and asks again on its next interval.
/// </para>
/// </remarks>
public sealed class UnlinkedStoredFileSweep
{
    /// <summary>The age below which a file is never removed, which is far longer than writing a file and linking it takes.</summary>
    public static readonly TimeSpan MinimumFileAge = TimeSpan.FromHours(1);

    /// <summary>How many candidates one run decides about at most.</summary>
    public const int MaximumFilesPerRun = 100;

    /// <summary>The lease the sweep is held under, which is the deployment's as the sweep is.</summary>
    internal static readonly WorkScope SweepScope = WorkScope.Create("unlinked-stored-file-sweep");

    private readonly AccessAuthorization authorization;
    private readonly IStoredFileStore files;
    private readonly IUserRecordFileLinks links;
    private readonly IWorkLeaseRunner leases;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the sweep.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="files">Names the candidates and removes what nothing links to.</param>
    /// <param name="links">Reads which files each candidate's owner links to.</param>
    /// <param name="leases">Holds the sweep's scope for the length of one run.</param>
    /// <param name="timeProvider">Reads the instant the age floor is measured from.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public UnlinkedStoredFileSweep(
        AccessAuthorization authorization,
        IStoredFileStore files,
        IUserRecordFileLinks links,
        IWorkLeaseRunner leases,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.files = files;
        this.links = links;
        this.leases = leases;
        this.timeProvider = timeProvider;
    }

    /// <summary>Runs one bounded sweep if this replica takes its lease.</summary>
    /// <param name="cancellationToken">Cancels the claim and the run.</param>
    /// <returns>How many files the run removed, or <see langword="null" /> when another replica holds the sweep.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when anything but this deployment's own process reached the use case.</exception>
    public async Task<int?> RunAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequireProcessIdentity();

        var removed = 0;

        var ran = await this.leases.TryRunUnderLeaseAsync(
            SweepScope,
            async heldToken => removed = await this.SweepAsync(heldToken),
            cancellationToken);

        return ran ? removed : null;
    }

    private async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var candidates = await this.files.FindUnmentionedAsync(
            this.timeProvider.GetUtcNow() - MinimumFileAge,
            MaximumFilesPerRun,
            cancellationToken);

        var removed = 0;

        // One read of a record per owner rather than per file, and awaited in order because each removal commits on its
        // own: a run cancelled part-way has removed exactly what it counted.
        foreach (var owned in candidates.GroupBy(candidate => candidate.Owner))
        {
            var linked = await this.links.ReadLinkedFilesAsync(owned.Key, cancellationToken);

            foreach (var unlinked in owned.Where(candidate => !linked.Contains(candidate.File)))
            {
                await this.files.RemoveAsync(owned.Key, unlinked.File, cancellationToken);
                removed++;
            }
        }

        return removed;
    }
}
