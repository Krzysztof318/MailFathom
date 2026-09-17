// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Provisioning;

/// <summary>One deployment-provisioned YAML file, as a source that says which layer put it there.</summary>
/// <remarks>
/// A file source like the framework's JSON one, so the file provider, the required-file check, and reload on change
/// are the framework's own and behave exactly as they do for <see cref="ProvisionedJsonConfigurationSource" />. Its
/// type is what the readers that tell a provisioned file apart from an operator's override recognize, for the reason
/// that type gives.
/// </remarks>
internal sealed class ProvisionedYamlConfigurationSource : FileConfigurationSource
{
    /// <inheritdoc />
    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        this.EnsureDefaults(builder);

        return new ProvisionedYamlConfigurationProvider(this);
    }
}
