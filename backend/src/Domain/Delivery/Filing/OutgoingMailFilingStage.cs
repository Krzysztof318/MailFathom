// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Filing;

/// <summary>States how far one filing of an outgoing message has durably got.</summary>
/// <remarks>
/// <para>
/// An <c>APPEND</c> is the one command in a filing that must never be issued twice: a repeat is a second message in the
/// owner's folder rather than a repeat of the first, and nothing the folder shows afterwards tells the two apart. So
/// the row reaches <see cref="Issued" /> before the command goes out and <see cref="Confirmed" /> after the server has
/// answered, and a row found at <see cref="Issued" /> is never appended again.
/// </para>
/// <para>
/// It is the same discipline the mutation record uses, for the same reason, and it is stored as its name so an ad-hoc
/// audit query stays readable and a later reordering of this enum changes nothing.
/// </para>
/// </remarks>
public enum OutgoingMailFilingStage
{
    /// <summary>The append has begun and the server's answer to it has not been read.</summary>
    /// <remarks>
    /// A row left here by a stopped process describes a folder that may or may not hold the copy. It is reported rather
    /// than repeated, because the only way to settle it without the server's own answer would be to search the folder
    /// for something that looks like the message, which is a guess about identity rather than a fact.
    /// </remarks>
    Issued = 0,

    /// <summary>The server accepted the append, and named where it put the copy wherever it advertises <c>UIDPLUS</c>.</summary>
    Confirmed = 1,

    /// <summary>The copy has been taken back out of the folder, which only the outbox mirror ever is.</summary>
    Withdrawn = 2,
}
