// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Transport;

/// <summary>Identifies a transport security rule that a configured mail account violates.</summary>
/// <remarks>
/// Violations are stable machine-readable identities so a configuration boundary can produce its own safe operator
/// message. A violation never carries the offending value, because the values involved include credentials and secret
/// references.
/// </remarks>
public enum MailTransportSecurityViolation
{
    /// <summary>No SASL mechanism is permitted, so no authentication could be attempted.</summary>
    PermittedAuthenticationMechanismRequired = 0,

    /// <summary>An unencrypted connection was selected without the explicit insecure-connection opt-in.</summary>
    UnencryptedConnectionRequiresExplicitOptIn = 1,

    /// <summary>A connection mode that silently continues unencrypted was selected without the explicit insecure-connection opt-in.</summary>
    OpportunisticEncryptionRequiresExplicitOptIn = 2,

    /// <summary>A clear-text mechanism is permitted on a channel that can stay unencrypted without both explicit opt-ins.</summary>
    ClearTextAuthenticationRequiresEncryptedConnection = 3,

    /// <summary>An additional trusted certificate authority was selected without a reference to its material.</summary>
    TrustedCertificateAuthorityReferenceRequired = 4,

    /// <summary>A trusted certificate authority reference was supplied while only the system trust store is used.</summary>
    TrustedCertificateAuthorityReferenceNotApplicable = 5,

    /// <summary>The selected connection security mode is not one of the supported modes.</summary>
    /// <remarks>Reachable because configuration can bind an enum from a raw number that names no member.</remarks>
    ConnectionSecurityNotSupported = 6,

    /// <summary>The selected certificate trust source is not one of the supported sources.</summary>
    /// <remarks>Reachable because configuration can bind an enum from a raw number that names no member.</remarks>
    CertificateTrustNotSupported = 7,
}
