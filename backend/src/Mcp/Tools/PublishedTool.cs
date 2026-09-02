// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools.Categories;

namespace MailFathom.Mcp.Tools;

/// <summary>What one tool this surface publishes declares about itself, beside the name it answers to.</summary>
/// <param name="RequiredPermission">The capability a caller must hold to be offered the tool and to call it.</param>
/// <param name="Category">The kind of thing the tool is for, which is what a deployment selects by.</param>
/// <remarks>
/// The two halves travel together because they are asked about the same name at the same moment and are declared in the
/// same place. Splitting them into two lookups would be two lists a tool has to appear in, and the second one is the one
/// somebody forgets — which is exactly the drift <see cref="PublishedTools" /> exists to make impossible.
/// </remarks>
internal readonly record struct PublishedTool(MailFathomPermission RequiredPermission, McpToolCategory Category);
