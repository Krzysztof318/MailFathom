// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Folders;

/// <summary>What one bounded pass over a folder MailFathom no longer mirrors removed.</summary>
/// <param name="ErasedEmailCount">How many stored emails the pass removed, with everything derived from them.</param>
/// <param name="EmailsRemain">Whether the folder still holds stored mail for a later pass to reach.</param>
public sealed record MailFolderMirrorErasure(int ErasedEmailCount, bool EmailsRemain)
{
    /// <summary>Gets the result of a pass over a folder that had nothing stored.</summary>
    public static MailFolderMirrorErasure Nothing { get; } = new(ErasedEmailCount: 0, EmailsRemain: false);
}
