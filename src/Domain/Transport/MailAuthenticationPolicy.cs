// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Transport;

/// <summary>Defines which SASL mechanisms a mail account may authenticate with and which weakenings are permitted.</summary>
/// <remarks>
/// The permitted set is an allow-list, never a preference hint: a transport adapter must remove every other mechanism
/// from the server's advertised set before authenticating and must not widen the set again when authentication fails.
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

    /// <summary>Gets the permitted mechanisms in configured preference order, without duplicates.</summary>
    public IReadOnlyList<MailAuthenticationMechanism> PermittedMechanisms { get; }

    /// <summary>Gets whether the operator accepted a connection mode that can leave the channel unencrypted.</summary>
    public bool AllowInsecureConnection { get; }

    /// <summary>Gets whether the operator accepted sending a reusable password over an unencrypted channel.</summary>
    public bool AllowClearTextAuthenticationOverUnencryptedConnection { get; }

    /// <summary>Gets whether any permitted mechanism sends the password in clear text.</summary>
    public bool PermitsClearTextCredentials => this.PermittedMechanisms.Any(mechanism => mechanism.TransmitsCredentialsInClearText());

    /// <summary>Creates an authentication policy from configured mechanisms and opt-ins.</summary>
    /// <param name="permittedMechanisms">The permitted mechanisms in preference order.</param>
    /// <param name="allowInsecureConnection">Whether a connection mode that can stay unencrypted is accepted.</param>
    /// <param name="allowClearTextAuthenticationOverUnencryptedConnection">Whether clear-text credentials on an unencrypted channel are accepted.</param>
    /// <returns>A policy whose mechanism list is deduplicated and keeps first-occurrence order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permittedMechanisms" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no mechanism remains after normalization.</exception>
    public static MailAuthenticationPolicy Create(
        IEnumerable<MailAuthenticationMechanism> permittedMechanisms,
        bool allowInsecureConnection,
        bool allowClearTextAuthenticationOverUnencryptedConnection)
    {
        ArgumentNullException.ThrowIfNull(permittedMechanisms);

        var normalizedMechanisms = NormalizeMechanisms(permittedMechanisms);
        if (normalizedMechanisms.Count == 0)
        {
            throw new ArgumentException("At least one permitted SASL mechanism is required.", nameof(permittedMechanisms));
        }

        return new MailAuthenticationPolicy(
            normalizedMechanisms,
            allowInsecureConnection,
            allowClearTextAuthenticationOverUnencryptedConnection);
    }

    /// <summary>Removes duplicates while keeping the configured preference order.</summary>
    /// <param name="permittedMechanisms">The configured mechanisms.</param>
    /// <returns>The normalized mechanism list.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permittedMechanisms" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<MailAuthenticationMechanism> NormalizeMechanisms(IEnumerable<MailAuthenticationMechanism> permittedMechanisms)
    {
        ArgumentNullException.ThrowIfNull(permittedMechanisms);

        return permittedMechanisms.Distinct().ToArray();
    }
}
