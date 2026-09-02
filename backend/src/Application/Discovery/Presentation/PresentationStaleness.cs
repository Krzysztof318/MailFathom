// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Says how far behind the mail server the data a block rests on may be.</summary>
/// <remarks>
/// Every block is composed from the local copy, which is what keeps a question from waiting on IMAP. The cost of that
/// is that the copy can be behind, and a block presenting last week's state of a conversation as the current one is
/// wrong in a way the reader cannot see. Saying which of the three it is turns that into something a client can draw.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PresentationStaleness>))]
public enum PresentationStaleness
{
    /// <summary>The local copy of everything behind the block was current when the run read it.</summary>
    Current = 0,

    /// <summary>Something behind the block is known to be behind the mail server.</summary>
    Stale = 1,

    /// <summary>Nothing established how current the local copy was, so the block claims neither.</summary>
    Unknown = 2,
}
