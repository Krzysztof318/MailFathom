// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.EmailContent;
using MailMcp.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Records email content repair requests in PostgreSQL, one row per affected email.</summary>
/// <remarks>
/// <para>
/// The write is a single <c>INSERT ... ON CONFLICT DO UPDATE</c> rather than a read followed by an insert or an update.
/// PostgreSQL resolves the collision itself, so two readers meeting the same damaged message concurrently leave one row
/// and one accurate count instead of one of them failing on the primary key. That is the idempotency the port promises,
/// expressed in the constraint rather than in a retry.
/// </para>
/// <para>
/// It is also why the statement is issued directly instead of through the change tracker: this runs on a read path,
/// which holds no persistence session, and calling <c>SaveChanges</c> on the scoped context would commit whatever else
/// that scope happened to have pending.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailContentRepairRequestStore(MailMcpDbContext dbContext, TimeProvider timeProvider)
    : IEmailContentRepairRequestStore
{
    /// <inheritdoc />
    public async Task RecordAsync(EmailContentRepairRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var storedEmailId = request.StoredEmailId.Value;

        // The name the defect is stored under, for the reason the content-availability reason is stored as text: it
        // stays readable in an audit query and survives any later reordering of the enum.
        var defect = request.Defect.ToString();
        var observedAt = timeProvider.GetUtcNow();

        // The table and column names are the ones the model declares; every value is a parameter. The identifiers are
        // quoted because EF Core names the columns after the properties, which PostgreSQL would otherwise fold to
        // lower case and fail to find.
        //
        // Each caller reads its own clock before this statement runs, so the one that observed the defect earlier can
        // still reach the conflict update last. Taking the later of the two timestamps, and keeping the defect of
        // whichever observation is the more recent, is what stops a straggler from rewriting the row with what it saw
        // before — and from leaving a last sighting that predates the first one. The count is incremented either way,
        // because both callers did meet the defect.
        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO email_content_repair_requests
                 ("StoredEmailId", "Defect", "FirstRequestedAt", "LastRequestedAt", "RequestCount")
             VALUES ({storedEmailId}, {defect}, {observedAt}, {observedAt}, 1)
             ON CONFLICT ("StoredEmailId") DO UPDATE SET
                 "Defect" = CASE
                     WHEN EXCLUDED."LastRequestedAt" >= email_content_repair_requests."LastRequestedAt"
                     THEN EXCLUDED."Defect"
                     ELSE email_content_repair_requests."Defect"
                 END,
                 "LastRequestedAt" = GREATEST(
                     email_content_repair_requests."LastRequestedAt",
                     EXCLUDED."LastRequestedAt"),
                 "RequestCount" = email_content_repair_requests."RequestCount" + 1
             """,
            cancellationToken);
    }
}
