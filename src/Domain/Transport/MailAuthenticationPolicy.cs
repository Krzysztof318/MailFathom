// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.ObjectModel;

namespace MailFathom.Domain.Transport;

/// <summary>Defines which SASL mechanisms a mail account may authenticate with and which weakenings are permitted.</summary>
/// <remarks>
/// The permitted set is an allow-list and is deliberately unordered: a transport adapter must remove every other
/// mechanism from the server's advertised set before authenticating, must not widen the set again when authentication
/// fails, and is free to pick the strongest survivor. Honoring a configured order would let a careless list put a
/// weaker mechanism ahead of a stronger one that both sides support. The list keeps its configured order only so
/// diagnostics and equality stay deterministic.
/// </remarks>
public sealed record MailAuthenticationPolicy
{
    private MailAuthenticationPolicy(
        IReadOnlyList<MailAuthenticationMechanism> permittedMechanisms,
        bool allowInsecureConnection,
        bool allowClearTextAuthenticationOverUnencryptedConnection)
    {
        this.PermittedMechanisms = permittedMechanisms;
        this.AllowInsecureConnection = allowInsecureConnection;
        this.AllowClearTextAuthenticationOverUnencryptedConnection = allowClearTextAuthenticationOverUnencryptedConnection;
    }

    /// <summary>Gets the permitted mechanisms in configured order, without duplicates.</summary>
    /// <remarks>The order is presentation only; see the type remarks for why it is not a preference.</remarks>
    public IReadOnlyList<MailAuthenticationMechanism> PermittedMechanisms { get; }

    /// <summary>Gets whether the operator accepted a connection mode that can leave the channel unencrypted.</summary>
    public bool AllowInsecureConnection { get; }

    /// <summary>Gets whether the operator accepted sending a reusable password over an unencrypted channel.</summary>
    public bool AllowClearTextAuthenticationOverUnencryptedConnection { get; }

    /// <summary>Gets whether any permitted mechanism sends the password in clear text.</summary>
    public bool PermitsClearTextCredentials => this.PermittedMechanisms.Any(mechanism => mechanism.TransmitsCredentialsInClearText);

    /// <summary>Creates an authentication policy from configured mechanisms and opt-ins.</summary>
    /// <param name="permittedMechanisms">The permitted mechanisms.</param>
    /// <param name="allowInsecureConnection">Whether a connection mode that can stay unencrypted is accepted.</param>
    /// <param name="allowClearTextAuthenticationOverUnencryptedConnection">Whether clear-text credentials on an unencrypted channel are accepted.</param>
    /// <returns>A policy whose mechanism list is deduplicated and keeps first-occurrence order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permittedMechanisms" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a value is the unspecified struct default or when no mechanism remains after normalization.</exception>
    public static MailAuthenticationPolicy Create(
        IEnumerable<MailAuthenticationMechanism> permittedMechanisms,
        bool allowInsecureConnection,
        bool allowClearTextAuthenticationOverUnencryptedConnection)
    {
        ArgumentNullException.ThrowIfNull(permittedMechanisms);

        var normalizedMechanisms = NormalizeMechanisms(permittedMechanisms);
        if (normalizedMechanisms.Any(mechanism => !mechanism.IsSpecified))
        {
            throw new ArgumentException("A permitted SASL mechanism must be one of the supported values.", nameof(permittedMechanisms));
        }

        if (normalizedMechanisms.Count == 0)
        {
            throw new ArgumentException("At least one permitted SASL mechanism is required.", nameof(permittedMechanisms));
        }

        return new MailAuthenticationPolicy(
            normalizedMechanisms,
            allowInsecureConnection,
            allowClearTextAuthenticationOverUnencryptedConnection);
    }

    /// <summary>Removes duplicates while keeping the configured order.</summary>
    /// <param name="permittedMechanisms">The configured mechanisms.</param>
    /// <returns>A read-only view that cannot be cast back to a mutable collection.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permittedMechanisms" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The result is wrapped rather than returned as an array, because a caller could otherwise cast the advertised
    /// <see cref="IReadOnlyList{T}" /> back to the array and replace a validated mechanism with a clear-text one after
    /// the policy accepted it.
    /// </remarks>
    public static IReadOnlyList<MailAuthenticationMechanism> NormalizeMechanisms(IEnumerable<MailAuthenticationMechanism> permittedMechanisms)
    {
        ArgumentNullException.ThrowIfNull(permittedMechanisms);

        return new ReadOnlyCollection<MailAuthenticationMechanism>(permittedMechanisms.Distinct().ToArray());
    }
}
