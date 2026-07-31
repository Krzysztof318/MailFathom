// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;

namespace MailMcp.Application.Synchronization;

/// <summary>Pairs one remote occurrence the server still holds with the flags it reported for it.</summary>
/// <param name="Uid">The UID the flags were read for, within the session's folder and UIDVALIDITY.</param>
/// <param name="Snapshot">What the server reported, and when it was read.</param>
/// <remarks>
/// An observation exists only for an email the server answered about. Absence is therefore the answer to a different
/// question than any flag can carry: the occurrence is no longer in the folder, which is what reconciliation acts on.
/// </remarks>
public sealed record RemoteEmailFlagObservation(ImapUid Uid, RemoteEmailFlagSnapshot Snapshot);
