// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>What has become of a draft a run composed, locally and nowhere else.</summary>
/// <remarks>
/// The three members are all local by construction, and the set deliberately holds no member meaning sent. A plan
/// describes what a run produced; sending is an act somebody takes afterwards, through the surface that governs
/// sending, and a presentation contract able to say "sent" would be a contract able to imply it happened here.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DraftDisposition>))]
public enum DraftDisposition
{
    /// <summary>The run composed it and nothing has been written down; it exists only in this plan.</summary>
    Composed = 0,

    /// <summary>It was written into the owner's drafts, where they can find it without this plan.</summary>
    Saved = 1,

    /// <summary>It is waiting in the outbox for whatever governs sending to act on it.</summary>
    Queued = 2,
}
