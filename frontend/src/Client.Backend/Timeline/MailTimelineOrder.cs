// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Timeline;

/// <summary>Which end of the message list leads.</summary>
/// <remarks>
/// The client's own copy of the two words the timeline route publishes, for the reason every wire record here is the
/// client's own: the contract is the format rather than a type shared across the two stacks. A value is written onto
/// the wire by <see cref="MailTimelineQuery" /> rather than by <see cref="Enum.ToString()" />, so renaming a member
/// here cannot silently change what a request says.
/// </remarks>
public enum MailTimelineOrder
{
    /// <summary>The newest mail first, which is what a list nobody has reordered shows.</summary>
    NewestFirst = 0,

    /// <summary>The oldest mail first.</summary>
    OldestFirst = 1,
}
