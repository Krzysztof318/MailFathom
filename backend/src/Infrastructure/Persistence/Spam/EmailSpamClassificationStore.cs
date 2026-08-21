// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>EF Core state for the one classification an occurrence carries.</summary>
/// <remarks>
/// The read joins no session, because it participates in no write. The write takes the caller's session so the
/// classification and the signals it rests on reach the database together, which is what keeps a record from ever being
/// readable beside the facts of the verdict it replaced.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailSpamClassificationStore(MailFathomDbContext dbContext) : IEmailSpamClassificationStore
{
    /// <inheritdoc />
    public async Task<SpamClassification?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken)
    {
        var storedEmailId = emailId.Value;
        var stored = await dbContext.EmailSpamClassifications
            .AsNoTracking()
            .Include(classification => classification.Signals)
            .SingleOrDefaultAsync(
                classification => classification.StoredEmailId == storedEmailId,
                cancellationToken);

        return stored is null ? null : EmailSpamClassificationMapping.Read(stored);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        IPersistenceSession session,
        SpamClassification classification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classification);

        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var storedEmailId = classification.EmailId.Value;

        // A primary-key lookup, so FindAsync already resolves a row this session staged itself — which is what stops a
        // session classifying one occurrence twice from adding a second row under the one key it may hold.
        var stored = await sessionContext.EmailSpamClassifications.FindAsync([storedEmailId], cancellationToken);

        if (stored is null)
        {
            stored = new EmailSpamClassificationEntity { StoredEmailId = storedEmailId };
            sessionContext.EmailSpamClassifications.Add(stored);
        }
        else
        {
            // FindAsync cannot eager-load, and replacing a verdict deletes the signals of the one it replaces: staging
            // the new ones beside rows nobody removed would violate the ordinal index rather than replace anything.
            var signals = sessionContext.Entry(stored).Collection(existing => existing.Signals);

            if (!signals.IsLoaded)
            {
                await signals.LoadAsync(cancellationToken);
            }

            sessionContext.EmailSpamClassificationSignals.RemoveRange(stored.Signals);
            stored.Signals.Clear();
        }

        EmailSpamClassificationMapping.Write(stored, classification);

        foreach (var signal in EmailSpamClassificationMapping.SignalRows(classification))
        {
            stored.Signals.Add(signal);
        }
    }
}
