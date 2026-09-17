// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Provisioning;

/// <summary>One deployment-provisioned configuration file, with the format it is read in.</summary>
/// <param name="Path">The file's path, as the deployment named or mounted it.</param>
/// <param name="Format">The syntax the file is read in, decided by its extension.</param>
internal sealed record ProvisionedConfigurationFile(string Path, ProvisionedConfigurationFormat Format)
{
    /// <summary>Says which format a file name's extension selects.</summary>
    /// <param name="fileName">The file name or path.</param>
    /// <returns>The format, or <see langword="null" /> when the extension is neither JSON nor YAML.</returns>
    /// <remarks>
    /// The extension decides rather than the content, because a file that parses as both — every JSON document is
    /// also a YAML one — would otherwise be read under whichever rules were tried first.
    /// </remarks>
    public static ProvisionedConfigurationFormat? FormatOf(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName);

        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ? ProvisionedConfigurationFormat.Json
            : extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) ? ProvisionedConfigurationFormat.Yaml
            : null;
    }

    /// <summary>Names the file and its format, as the startup record reports it.</summary>
    /// <returns>The path followed by the format in parentheses.</returns>
    public override string ToString() => $"{this.Path} ({this.Format})";
}
