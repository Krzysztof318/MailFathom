// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail;
using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Hands MimeKit the key a signing domain publishes, resolved through the application's own port.</summary>
/// <remarks>
/// <para>
/// MimeKit implements no DNS of its own and asks a locator for each key it needs, so this is the join between the
/// verification and the resolver. It is deliberately the only place the two meet: the port above it sees a selector, a
/// domain, and a record's text, and the cryptographic type below it never travels any further than the verifier that
/// asked for it.
/// </para>
/// <para>
/// The contract is a key or an exception — there is no way to answer "no key" — so an unresolvable or unparseable
/// record is signalled as <see cref="DkimPublicKeyUnavailableException" /> and caught by the verifier, which records a
/// verdict that established nothing. Both halves are the same fact: nothing about the sender was learned, and neither
/// case is a statement against their mail.
/// </para>
/// </remarks>
internal sealed class ResolvedDkimPublicKeyLocator : DkimPublicKeyLocatorBase
{
    private readonly IDkimPublicKeyRecordResolver resolver;

    /// <summary>Initializes a locator over the port that resolves published records.</summary>
    /// <param name="resolver">Resolves what a signing domain publishes for a selector.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver" /> is <see langword="null" />.</exception>
    public ResolvedDkimPublicKeyLocator(IDkimPublicKeyRecordResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        this.resolver = resolver;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always, because resolution here is asynchronous.</exception>
    /// <remarks>
    /// Every verification this system performs is asynchronous, so MimeKit reaches the overload below and never this
    /// one. Blocking on the resolution to satisfy the signature is what the refusal exists to prevent: it would put a
    /// synchronous DNS wait on whichever thread happened to extract a message.
    /// </remarks>
    [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "The synchronous half of the library's contract is unreachable here, and blocking on the asynchronous resolution to satisfy it is the outcome the refusal prevents.")]
    public override AsymmetricKeyParameter LocatePublicKey(
        string methods,
        string domain,
        string selector,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("DKIM public keys are resolved asynchronously, so the synchronous locator is never used.");

    /// <inheritdoc />
    /// <exception cref="DkimPublicKeyUnavailableException">Thrown when nothing usable is published for the selector.</exception>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The record is arbitrary text from a nameserver this deployment does not control, and every way of failing to read it as a key is the same fact: no key was obtained. The reader is a third-party PEM parser whose failure modes are not enumerated, so narrowing the catch would leave one of them escaping extraction.")]
    public override async Task<AsymmetricKeyParameter> LocatePublicKeyAsync(
        string methods,
        string domain,
        string selector,
        CancellationToken cancellationToken = default)
    {
        var record = await this.resolver.ResolveAsync(selector, domain, cancellationToken);

        if (record is null)
        {
            throw new DkimPublicKeyUnavailableException();
        }

        try
        {
            return GetPublicKey(record);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A record that is published and unreadable is the same fact as one that is absent: no key was obtained,
            // so nothing about this signature can be established either way. The caller's own cancellation is the one
            // thing that still travels, because that is this process giving up rather than a stranger's record being
            // unusable.
            throw new DkimPublicKeyUnavailableException(exception);
        }
    }
}

/// <summary>Reports that no usable key stands behind a signature, which the locator's contract cannot say otherwise.</summary>
/// <remarks>
/// <see cref="IDkimPublicKeyLocator" /> answers with a key or raises, so the absence has to travel as an exception. It
/// is caught by the verifier that started the verification and never leaves the adapter, which is why it carries no
/// error code: a failure identity names something a boundary publishes, and nothing publishes this.
/// </remarks>
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "The type is a control-flow signal from the locator to the verifier that asked for the key, caught there and never observed at any boundary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "The absence of a key carries no message of its own, and the only cause worth keeping is the parse failure the second constructor takes.")]
[SuppressMessage("Usage", "RCS1194:Implement exception constructors", Justification = "The absence of a key carries no message of its own, and the only cause worth keeping is the parse failure the second constructor takes.")]
internal sealed class DkimPublicKeyUnavailableException : Exception
{
    /// <summary>Initializes the signal for a selector nothing usable is published at.</summary>
    public DkimPublicKeyUnavailableException()
        : base("No usable DKIM public key is published for the selector.")
    {
    }

    /// <summary>Initializes the signal for a record that was published and could not be read as a key.</summary>
    /// <param name="innerException">The parse failure the record produced.</param>
    public DkimPublicKeyUnavailableException(Exception innerException)
        : base("The published DKIM key record could not be read as a key.", innerException)
    {
    }
}
