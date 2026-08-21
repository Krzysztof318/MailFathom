// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization;

/// <summary>Represents one bounded mailbox metadata page and the non-speculative UID cursor that has been inspected.</summary>
/// <param name="Emails">The emails discovered in this bounded page.</param>
/// <param name="InspectedThroughUid">The highest UID value known safe to checkpoint, or <see langword="null" /> when no assigned UID was inspected.</param>
/// <param name="HasMore">Whether the session may have more UID windows to inspect after this page.</param>
public sealed record RemoteEmailMetadataBatch(
    IReadOnlyList<RemoteEmailMetadata> Emails,
    ImapUid? InspectedThroughUid,
    bool HasMore);
