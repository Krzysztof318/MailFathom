// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.AiProviders;

/// <summary>Names the bulk workloads this deployment paces against a provider's declared rate.</summary>
/// <remarks>
/// <para>
/// Each name is the key one <see cref="IProviderPaceMarker" /> row is held under, so it is written once here and
/// survives every rename the code it paces ever takes — the same reason a backfill walk names itself rather than being
/// named after the type that walks it.
/// </para>
/// <para>
/// There are two because there are two quotas. Embedding a mailbox's passages and describing its pictures reach two
/// declared endpoints with two rates, and one marker shared between them would let either workload's burst spend the
/// other's slots.
/// </para>
/// </remarks>
public static class ProviderPacedWorkloads
{
    /// <summary>The workload that embeds this deployment's mail, paced by the embedding provider's declared rate.</summary>
    public const string EmailEmbedding = "email-embedding";

    /// <summary>The workload that describes this deployment's picture attachments, paced by the chat provider's own rate.</summary>
    public const string AttachmentImageDescription = "attachment-image-description";

    /// <summary>The greatest length a workload name may take, which is what the key it is held under is sized for.</summary>
    public const int MaxNameLength = 64;
}
