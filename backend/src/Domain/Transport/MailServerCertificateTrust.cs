// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Transport;

/// <summary>Selects which certificate authorities are trusted when validating a mail server certificate.</summary>
/// <remarks>
/// Certificate validation itself is never optional. A private or self-signed server is supported by trusting an
/// additional authority, never by disabling validation.
/// </remarks>
public enum MailServerCertificateTrust
{
    /// <summary>Validates the server certificate against the operating-system trust store only.</summary>
    SystemTrustStore = 0,

    /// <summary>Validates the server certificate against the system trust store plus a deployment-provisioned authority.</summary>
    AdditionalTrustedAuthority = 1,
}
