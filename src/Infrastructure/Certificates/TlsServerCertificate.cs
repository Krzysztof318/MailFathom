// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Infrastructure.Certificates;

/// <summary>A validated TLS identity: the leaf a server presents, its private key, and the authorities presented after it.</summary>
/// <remarks>
/// <para>
/// The intermediates travel with the leaf because a client that cannot build a path to a trusted root rejects the
/// handshake, and the issuing intermediate is routinely absent from a client's own store. They carry no trust of their
/// own; presenting them only saves the client from having to find them.
/// </para>
/// <para>
/// The instance owns every certificate it holds and releases them all on disposal. Nothing here is secret in the sense
/// the secret machinery uses — a certificate is public material — but the leaf holds a private key, so an instance is
/// kept for as long as the endpoint serves and disposed when it stops rather than being cloned per connection.
/// </para>
/// </remarks>
public sealed class TlsServerCertificate : IDisposable
{
    private readonly X509Certificate2[] intermediates;
    private bool disposed;

    internal TlsServerCertificate(X509Certificate2 leaf, X509Certificate2[] intermediates)
    {
        this.Leaf = leaf;
        this.intermediates = intermediates;
    }

    /// <summary>Gets the leaf certificate, which carries the private key the handshake signs with.</summary>
    public X509Certificate2 Leaf { get; }

    /// <summary>Gets the intermediate certificates presented after the leaf, in the order they chain towards a root.</summary>
    /// <remarks>Empty when the material supplied none, which is correct for a leaf issued directly by a root a client already trusts.</remarks>
    public IReadOnlyList<X509Certificate2> Intermediates => this.intermediates;

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.Leaf.Dispose();

        foreach (var intermediate in this.intermediates)
        {
            intermediate.Dispose();
        }
    }
}
