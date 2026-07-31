// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Infrastructure.Mail;

/// <summary>Everything one account's connection attempt resolved from its configured references.</summary>
/// <param name="Password">The mailbox password or app password.</param>
/// <param name="TrustedCertificateAuthority">
/// The deployment-provisioned authority the server certificate must chain to, or <see langword="null" /> when the
/// account validates against the system trust store alone.
/// </param>
/// <remarks>
/// <para>
/// The instance is owned by the operation that resolved it — one connection attempt, or one startup validation pass —
/// and must be disposed when that operation ends, which bounds the window in which a process dump could contain the
/// password to an operation rather than to process uptime. Because every operation resolves its own instance,
/// publishing a new configuration snapshot never erases material an in-flight operation is still reading, and a
/// credential or anchor rotated behind an unchanged reference is picked up by the next connection.
/// </para>
/// <para>
/// It is named material rather than secrets because only one of its members is one. A trust anchor is a public
/// certificate that may be logged by subject and thumbprint; it travels here because it shares the password's
/// per-operation ownership and disposal rule, not its confidentiality.
/// </para>
/// </remarks>
public sealed record MailAccountConnectionMaterial(
    ResolvedSecret Password,
    X509Certificate2? TrustedCertificateAuthority) : IDisposable
{
    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        this.Password.Dispose();
        this.TrustedCertificateAuthority?.Dispose();
    }
}
