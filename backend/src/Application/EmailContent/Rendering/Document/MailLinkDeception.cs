// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>What the service established about a link's text against the place the link actually goes.</summary>
/// <remarks>
/// It is determined once, here, rather than by each renderer. Two clients deriving it for themselves would be two
/// chances to derive it differently, and the quieter of the two would be the one a reader was unlucky enough to have.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MailLinkDeception>))]
public enum MailLinkDeception
{
    /// <summary>The link's text is not a place, so there is nothing for it to disagree with.</summary>
    NotApplicable = 0,

    /// <summary>The link's text names a place and it is the place the link goes.</summary>
    None = 1,

    /// <summary>The link's text names one host and the link goes to another, which is the oldest trick in mail.</summary>
    DisplayedHostDiffers = 2,
}
