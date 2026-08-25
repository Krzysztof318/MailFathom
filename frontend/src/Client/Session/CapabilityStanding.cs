// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Session;

/// <summary>Why a capability is or is not offered, which is not one question but two.</summary>
/// <remarks>
/// Conflating the two produces the wrong message and sends somebody looking for a permission nobody can give them. A
/// grant is about this caller — their credential does not permit asking, so the question field is not there — and a
/// deployment feature is about this installation, which has nothing to answer with however the caller is granted. The
/// interface says which of the two it is, in the words a person can act on.
/// </remarks>
public enum CapabilityStanding
{
    /// <summary>The deployment provides it and this caller's grant carries it, so the interface offers it.</summary>
    Offered = 0,

    /// <summary>The deployment provides it and this caller's credential does not carry it.</summary>
    Ungranted = 1,

    /// <summary>This deployment does not provide it at all, so no grant would make it available.</summary>
    Unavailable = 2,
}
