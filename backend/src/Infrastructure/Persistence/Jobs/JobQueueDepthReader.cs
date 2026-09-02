// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Measures the depth of each type's queue in PostgreSQL, through the same reading the enqueue bound uses.</summary>
/// <remarks>
/// <para>
/// One bounded count per type rather than one grouped count over the table, and that is the point rather than an
/// inefficiency: <see cref="JobQueueDepthQuery" /> stops reading at the configured bound, so the cost of measuring is
/// the same whatever has accumulated behind it. A grouped count would be an index scan over the whole pending set, on
/// whatever interval the worker happens to poll at.
/// </para>
/// <para>
/// Sharing that query with the enqueue path is what keeps the number an operator watches and the number an enqueue is
/// refused at the same number. Two readings that counted different states would make a queue look full on a dashboard
/// while it accepted work, or the reverse.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class JobQueueDepthReader(MailFathomDbContext dbContext, JobCapacitySettings capacity)
    : IJobQueueDepthReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<JobQueueDepthReading>> ReadWaitingDepthsAsync(
        IReadOnlyList<JobType> jobTypes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobTypes);

        var readings = new List<JobQueueDepthReading>(jobTypes.Count);

        foreach (var jobType in jobTypes)
        {
            var waitingCount = await JobQueueDepthQuery
                .Compose(dbContext.Jobs.AsNoTracking(), jobType.Name, capacity.MaxQueueDepthPerType)
                .CountAsync(cancellationToken);

            readings.Add(new JobQueueDepthReading(jobType, waitingCount));
        }

        return readings;
    }
}
