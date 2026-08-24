// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Move;

/// <summary>Names why the move left one payload exactly where it was.</summary>
/// <remarks>
/// <para>
/// Each member is a different act by the operator, which is the whole reason they are apart: a payload whose stored
/// bytes disagree with their own row is a defect in what the database holds, an object that came back wrong is a defect
/// in what the bucket holds, and a payload too large for the process to hold is a bound the operator configured.
/// </para>
/// <para>
/// An endpoint that could not answer at all is deliberately not here. It says nothing about the payload in front of the
/// pass, it is already classified by the object-storage instruments the request went through, and the answer to it is
/// to leave the position where it is and try the same payload again — not to count it as a payload that cannot move.
/// </para>
/// </remarks>
public enum StoredContentMoveFailure
{
    /// <summary>The stored payload's own length or digest disagrees with what its row records, so nothing was written.</summary>
    /// <remarks>
    /// Checked before the object is put rather than after, which is what keeps a payload nobody can vouch for out of the
    /// bucket entirely. Only a re-synchronization of the message can repair it, and for an outgoing record or a draft
    /// nothing can.
    /// </remarks>
    SourceMismatch = 0,

    /// <summary>The object came back with a length or a digest that is not the one the row records.</summary>
    /// <remarks>
    /// The row stays database-backed, so the message is still readable, and the object nothing points at is reclaimed
    /// as any other orphan is.
    /// </remarks>
    ObjectMismatch = 1,

    /// <summary>The endpoint answered, and held no object under the key the move had just written.</summary>
    ObjectAbsent = 2,

    /// <summary>The payload is larger than the whole raw MIME budget this process holds payloads within.</summary>
    /// <remarks>
    /// A payload stored under a larger bound than the one the deployment now runs with. Nothing is wrong with it, and
    /// raising <c>MailSynchronization:MaxInFlightRawMimeBytes</c> past its size is what lets a later move carry it.
    /// </remarks>
    Oversized = 3,
}
