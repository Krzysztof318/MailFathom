// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Policies;

/// <summary>One scope's settings policy as an editing session saved it.</summary>
/// <param name="Version">The version the buffer was opened over, which the commit is accepted against.</param>
/// <param name="Document">The policy as the operator left it, which the deployment judges whole.</param>
/// <remarks>
/// The whole policy rather than the difference, for the reason a user's record is saved whole: what the deployment
/// accepts is a document, and the version is what makes sending one safe — a buffer composed over a policy somebody
/// else has moved past is refused rather than merged.
/// </remarks>
internal sealed record SettingsPolicySaveRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("document")] string Document);
