// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Provisioning;

/// <summary>The syntax a deployment-provisioned configuration file is written in, as its extension names it.</summary>
/// <remarks>
/// Both formats flatten to the same colon-delimited keys, so everything after the read — binding, validation, secret
/// references, reload, and precedence — is one mechanism whichever of the two a file is written in.
/// </remarks>
internal enum ProvisionedConfigurationFormat
{
    /// <summary>A file ending in <c>.json</c>.</summary>
    Json = 0,

    /// <summary>A file ending in <c>.yaml</c> or <c>.yml</c>.</summary>
    Yaml = 1,
}
