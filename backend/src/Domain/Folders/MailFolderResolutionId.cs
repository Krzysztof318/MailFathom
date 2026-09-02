// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Identifies which binding of an alias a folder-scoped record belongs to.</summary>
/// <remarks>
/// This is the folder component of the stable remote occurrence identity, and it names a generation rather than an
/// alias on purpose: the tuple is only stable while its folder component identifies one specific remote folder, and
/// an alias can be repointed to another one. See <see cref="MailFolderResolutionGeneration" /> for what a shared
/// identity would cost.
/// </remarks>
/// <param name="Alias">The operator-facing folder name.</param>
/// <param name="Generation">The binding of that alias the record belongs to.</param>
public readonly record struct MailFolderResolutionId(MailFolderAlias Alias, MailFolderResolutionGeneration Generation)
{
    /// <inheritdoc />
    public override string ToString() => $"{this.Alias.Value}#{this.Generation.Value}";
}
