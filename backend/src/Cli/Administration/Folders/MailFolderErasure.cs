// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Folders;

/// <summary>What a deployment is asked when a folder's stored mail is to be erased.</summary>
/// <param name="Account">The account the folder belongs to, as the deployment's configuration names it.</param>
/// <param name="Folder">MailFathom's own alias for the folder, which need not be one the deployment still maps.</param>
internal sealed record MailFolderErasureRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("folder")] string Folder);

/// <summary>What one bounded pass of an erasure removed.</summary>
/// <param name="Account">The account the folder belongs to.</param>
/// <param name="Folder">The alias the pass ran against, as the deployment normalized it.</param>
/// <param name="ErasedEmailCount">How many stored emails the pass removed.</param>
/// <param name="EmailsRemain">Whether the folder still holds mail a further pass would reach.</param>
internal sealed record MailFolderErasure(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("erasedEmailCount")] int ErasedEmailCount,
    [property: JsonPropertyName("emailsRemain")] bool EmailsRemain);
