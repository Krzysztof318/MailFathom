// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Reads back where a server said it put a copy of a message, from the two columns that record it.</summary>
/// <remarks>
/// The two columns are written together and read together. A row carrying one of them is a row no code path here can
/// produce, and reading it as a placement would name a UID with no UID space to interpret it in — which is the one thing
/// a removal must never do. Every table that records a placement stores it this way, which is why the reading is one
/// method rather than one per table.
/// </remarks>
internal static class StoredRemotePlacement
{
    /// <summary>Reads the placement the two stored columns describe.</summary>
    /// <param name="uidValidity">The UID space the server named, where it named one.</param>
    /// <param name="uid">The UID the server named, where it named one.</param>
    /// <returns>The placement, or <see cref="RemoteEmailPlacement.NotReported" /> when either column is absent.</returns>
    internal static RemoteEmailPlacement Of(uint? uidValidity, uint? uid) =>
        uidValidity is { } storedUidValidity && uid is { } storedUid
            ? RemoteEmailPlacement.Reported(ImapUidValidity.Create(storedUidValidity), ImapUid.Create(storedUid))
            : RemoteEmailPlacement.NotReported();
}
