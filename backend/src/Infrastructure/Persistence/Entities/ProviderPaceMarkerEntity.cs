// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>When one paced workload of this deployment may send its next request to an AI provider.</summary>
/// <remarks>
/// <para>
/// One row per paced workload, keyed by the workload's own name. A rate is what a provider states about the whole
/// deployment, so the marker every replica moves has to be one row rather than a field in each of them — three replicas
/// pacing themselves would send three times the declared rate at a quota that answers with a refusal.
/// </para>
/// <para>
/// The names themselves are <c>ProviderPacedWorkloads</c>'s rather than this type's, because what a workload is belongs
/// above persistence: this row only has to store one. They are written down rather than derived from a type, for the
/// reason a backfill walk's name is — a key in an append-only schema has to survive every rename the code it paces
/// ever takes.
/// </para>
/// <para>
/// Nothing allocates a row. The first reservation of a workload inserts it and every later one moves it forward, which
/// is what lets a deployment that turns a workload on years later begin paced rather than begin with a burst.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. A workload name and an instant say when this deployment may next send, and
/// neither names a message, a user, or an account.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ProviderPaceMarkerEntity
{
    /// <summary>The table these rows live in, named here because the reservation is a composed statement.</summary>
    internal const string TableName = "provider_pace_markers";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string WorkloadColumnName = "Workload";

    /// <summary>The moved column, named here for the same reason the table is.</summary>
    internal const string NextSlotAtColumnName = "NextSlotAt";

    /// <summary>Gets or sets which paced workload this marker belongs to.</summary>
    public string Workload { get; set; } = string.Empty;

    /// <summary>Gets or sets when this workload's next request may be sent, in UTC.</summary>
    public DateTimeOffset NextSlotAt { get; set; }
}
