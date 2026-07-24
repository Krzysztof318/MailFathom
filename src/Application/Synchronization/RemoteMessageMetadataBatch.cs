// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Messages;

namespace MailMcp.Application.Synchronization;

/// <summary>Represents one bounded IMAP metadata page and the UID cursor that has been inspected.</summary>
/// <param name="Messages">The messages discovered in this bounded page.</param>
/// <param name="InspectedThroughUid">The highest UID value inspected by this page.</param>
/// <param name="HasMore">Whether the session may have more UID windows to inspect after this page.</param>
public sealed record RemoteMessageMetadataBatch(IReadOnlyList<RemoteMessageMetadata> Messages, ImapUid InspectedThroughUid, bool HasMore);
