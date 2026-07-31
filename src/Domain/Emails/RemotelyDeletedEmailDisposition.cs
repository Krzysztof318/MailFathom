// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Domain.Emails;

/// <summary>States what becomes of the local copy of an email the mail server no longer holds.</summary>
/// <remarks>
/// <para>
/// The choice is made once per observed disappearance, by the run that observes it, and never revisited. An email
/// already tombstoned under one disposition is outside every later reconciliation window — the server has nothing left
/// to report about it — so changing the configured disposition governs the disappearances observed from then on and
/// leaves what is already recorded exactly as it is.
/// </para>
/// <para>
/// Both values describe a purely local decision. Neither reaches the server: MailFathom reads mail read-only, and no path
/// here issues an IMAP <c>STORE</c> or <c>EXPUNGE</c>.
/// </para>
/// </remarks>
public enum RemotelyDeletedEmailDisposition
{
    /// <summary>Keeps the local row as a tombstone that every mailbox query excludes.</summary>
    /// <remarks>
    /// The record that the email existed survives, which is what makes a disappearance auditable rather than silent.
    /// It is the default because it is the reversible one: a server that misreports a folder costs a hidden row rather
    /// than a destroyed local copy.
    /// </remarks>
    RetainTombstone = 0,

    /// <summary>Removes the local row together with its raw MIME and everything derived from it.</summary>
    /// <remarks>
    /// Nothing of the email survives locally, so a deployment that must not retain mail past the server's own copy can
    /// say so. The removal happens as the disappearance is observed, which is deliberate and irreversible.
    /// </remarks>
    EraseLocalCopy = 1,
}
