// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Editing;

/// <summary>The syntax a document the deployment holds is shown and edited in.</summary>
/// <remarks>
/// A view and nothing more: the deployment stores and serves JSON whichever is chosen, and a YAML buffer is converted
/// back to JSON before anything is committed.
/// </remarks>
internal enum DocumentView
{
    /// <summary>The document as the deployment serves it.</summary>
    Json = 0,

    /// <summary>The document rendered as YAML.</summary>
    Yaml = 1,
}
