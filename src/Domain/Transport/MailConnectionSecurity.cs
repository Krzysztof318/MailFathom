// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Domain.Transport;

/// <summary>Selects how a mail transport connection is encrypted.</summary>
/// <remarks>
/// Only <see cref="TlsOnConnect" /> and <see cref="StartTlsRequired" /> guarantee that credentials and mail content
/// never travel over an unencrypted channel. The remaining modes can complete a connection without encryption, so a
/// transport security policy requires an explicit operator opt-in before selecting them.
/// </remarks>
public enum MailConnectionSecurity
{
    /// <summary>Lets the transport client negotiate encryption and continue unencrypted when the server offers none.</summary>
    Auto = 0,

    /// <summary>Encrypts the connection immediately with implicit TLS.</summary>
    TlsOnConnect = 1,

    /// <summary>Requires STARTTLS after the greeting and fails when the server does not advertise it.</summary>
    StartTlsRequired = 2,

    /// <summary>Uses STARTTLS when the server advertises it and otherwise continues unencrypted.</summary>
    StartTlsWhenAvailable = 3,

    /// <summary>Uses no encryption at all.</summary>
    None = 4,
}
