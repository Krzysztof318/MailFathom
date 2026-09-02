// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>Whether an attachment a plan found can actually be opened.</summary>
/// <remarks>
/// A gallery that offered every file alike would be offering some a reader cannot have: a message may be known from its
/// metadata while its content was never stored, and retention may have removed content the metadata outlived. Saying
/// which of the three it is turns a failed download into something the screen never offered.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AttachmentAvailability>))]
public enum AttachmentAvailability
{
    /// <summary>The content is held locally and the attachment can be opened now.</summary>
    Stored = 0,

    /// <summary>The message is known but its content was never stored, so opening it would mean fetching the message first.</summary>
    NotStored = 1,

    /// <summary>The content is gone and will not come back, which retention or an erasure decided.</summary>
    Removed = 2,
}
