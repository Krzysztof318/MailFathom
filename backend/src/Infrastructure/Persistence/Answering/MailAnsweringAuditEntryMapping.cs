// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Answering;

/// <summary>Turns one answering audit entry into the rows that keep it, and back.</summary>
internal static class MailAnsweringAuditEntryMapping
{
    /// <summary>Every degradation this build knows how to read, which a stored value may name no more than.</summary>
    private const MailAnsweringRunDegradation KnownDegradation =
        MailAnsweringRunDegradation.RetrievalCeilingReached | MailAnsweringRunDegradation.RelevanceFilterFellBack;

    /// <summary>Builds the rows one entry is kept as.</summary>
    /// <param name="entry">The entry to keep.</param>
    /// <returns>The row to append, with the emails it names already attached.</returns>
    internal static MailAnsweringAuditEntryEntity ToEntity(MailAnsweringAuditEntry entry)
    {
        var entity = new MailAnsweringAuditEntryEntity
        {
            Id = entry.Id.Value,
            RunId = entry.RunId.Value,
            MailboxAccountId = entry.AccountId.Value,
            OwnerId = entry.Owner.Value,
            ChatEndpointAlias = entry.ChatEndpointAlias,
            InstructionsVersion = entry.InstructionsVersion,
            StartedAt = entry.StartedAt,
            CompletedAt = entry.CompletedAt,
            Outcome = entry.Outcome.ToString(),
            Degradation = entry.Degradation.ToString(),
        };

        foreach (var email in entry.Emails)
        {
            entity.Emails.Add(new MailAnsweringAuditedEmailEntity
            {
                MailAnsweringAuditEntryId = entity.Id,
                StoredEmailId = email.StoredEmailId.Value,
                Position = email.Position,
                WasCited = email.WasCited,
            });
        }

        return entity;
    }

    /// <summary>Rebuilds the entry one stored row states, or reports a row this build cannot interpret.</summary>
    /// <param name="entity">The stored row, with its emails loaded.</param>
    /// <param name="entry">The entry that row states, when this build can read it.</param>
    /// <returns><see langword="true" /> when the row was rebuilt; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// <para>
    /// A row is refused rather than approximated when it names an ending or a degradation this build does not
    /// recognize — which is version skew rather than corruption: a later build that names a third way a run can degrade
    /// writes entries this one has no value for, and a rollback then reads them.
    /// </para>
    /// <para>
    /// It is reported rather than thrown because this record is read a page at a time and paginated by position, so one
    /// unreadable row thrown out of the mapping would fail the whole page and every page after it. The caller leaves the
    /// row out, says so, and walks on.
    /// </para>
    /// </remarks>
    internal static bool TryToEntry(
        MailAnsweringAuditEntryEntity entity,
        [NotNullWhen(true)] out MailAnsweringAuditEntry? entry)
    {
        entry = null;

        if (!TryToOutcome(entity.Outcome, out var outcome) || !TryToDegradation(entity.Degradation, out var degradation))
        {
            return false;
        }

        entry = new MailAnsweringAuditEntry
        {
            Id = MailAnsweringAuditEntryId.Create(entity.Id),
            RunId = MailAnsweringRunId.Create(entity.RunId),
            Account = MailAccountIdentity.Create(
                MailOwnerId.Create(entity.OwnerId),
                MailAccountId.Create(entity.MailboxAccountId)),
            Emails =
            [
                .. entity.Emails
                    .OrderBy(static email => email.Position)
                    .Select(static email => new MailAnsweringAuditedEmail(
                        StoredEmailId.Create(email.StoredEmailId),
                        email.Position,
                        email.WasCited)),
            ],
            ChatEndpointAlias = entity.ChatEndpointAlias,
            InstructionsVersion = entity.InstructionsVersion,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            Outcome = outcome,
            Degradation = degradation,
        };

        return true;
    }

    /// <summary>Reads back the ending a stored row names, refusing one this build declares no member for.</summary>
    /// <remarks>
    /// The definition check is what makes this more than a parse: the stored text is a member name, but the parser also
    /// accepts a number, and an ending nothing declares would otherwise be published as a value no reader could
    /// interpret.
    /// </remarks>
    private static bool TryToOutcome(string stored, out MailAnsweringRunOutcome outcome) =>
        Enum.TryParse(stored, out outcome) && Enum.IsDefined(outcome);

    /// <summary>Reads back the degradation a stored row names, refusing any way to degrade this build does not know.</summary>
    /// <remarks>
    /// A set cannot be checked with <see cref="Enum.IsDefined{TEnum}(TEnum)" />, which answers about single members, so
    /// the test is that the parsed value names no bit outside the ones this build declares.
    /// </remarks>
    private static bool TryToDegradation(string stored, out MailAnsweringRunDegradation degradation) =>
        Enum.TryParse(stored, out degradation)
        && (degradation & ~KnownDegradation) == MailAnsweringRunDegradation.None;
}
