// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Provisioning;

/// <summary>Reads a deployment-provisioned YAML file into the same keys the equivalent JSON file produces.</summary>
/// <param name="source">The source the provider was built from.</param>
/// <remarks>
/// A file the reader refuses surfaces as the framework reports a malformed JSON file: an
/// <see cref="InvalidDataException" /> naming the file, wrapping the <see cref="FormatException" /> that names the
/// position.
/// </remarks>
internal sealed class ProvisionedYamlConfigurationProvider(ProvisionedYamlConfigurationSource source)
    : FileConfigurationProvider(source)
{
    /// <inheritdoc />
    public override void Load(Stream stream) => this.Data = YamlConfigurationDocument.Flatten(stream);
}
