// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Infrastructure.Persistence.Settings;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Decides whether an owner's record may be handed to a statement at all.</summary>
/// <remarks>
/// Deliberately outside the writer that issues the statement, for the reason
/// <see cref="RootSettingsCommitRules" /> is: what the database decides needs a database to prove and belongs to the
/// integration suite, while whether a document may be handed to a statement at all is decidable here and belongs in
/// the unit suite's measurement. The measurement and the shape question are the deployment document's own rules, asked
/// of the same type so the two documents cannot come to be held to different ones; the bound they are asked against is
/// this document's.
/// </remarks>
internal static class OwnerSettingsCommitRules
{
    /// <summary>Refuses a candidate no statement should be issued for.</summary>
    /// <param name="json">The candidate record, as the JSON object the row would hold.</param>
    /// <param name="expectedVersion">The version the candidate was composed over.</param>
    /// <exception cref="ArgumentException">Thrown when the candidate is <see langword="null" />, empty, white space, not JSON, not an object, or past what the column binds from.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expectedVersion" /> is negative.</exception>
    /// <remarks>
    /// Every refusal is an <see cref="ArgumentException" /> because each is a candidate this build composed wrongly
    /// rather than anything the database has an opinion about.
    /// </remarks>
    internal static void RefuseWhatCannotBeCommitted(string json, long expectedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        int persistedOctets;

        try
        {
            if (!RootSettingsCommitRules.RootIsAnObject(json))
            {
                throw new ArgumentException(
                    "The candidate owner record is JSON whose root is not an object, so it carries no settings at all. The column would store it, and the next read would refuse it: only an object binds to an owner's record.",
                    nameof(json));
            }

            persistedOctets = RootSettingsCommitRules.PersistedOctetsOf(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The candidate owner record is not JSON, so no statement is issued for it. The column would take it no further: its jsonb cast refuses exactly this, and the refusal belongs on the side that composed the document.",
                nameof(json),
                exception);
        }

        if (persistedOctets > OwnerSettingsDocument.MaximumOctets)
        {
            throw new ArgumentException(
                $"The candidate owner record occupies {persistedOctets} octets as the database stores it, past the {OwnerSettingsDocument.MaximumOctets} this build binds an owner's record from, so persisting it would leave a row the next read refuses.",
                nameof(json));
        }
    }
}
