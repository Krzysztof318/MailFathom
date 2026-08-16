// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Mailboxes;

/// <summary>What one bounded pass of a re-derivation re-read.</summary>
/// <param name="Account">The account the pass ran against.</param>
/// <param name="Folder">The alias it was narrowed to, or nothing when it covered the whole account.</param>
/// <param name="RederivedEmailCount">How many stored emails the pass re-read and wrote metadata for.</param>
/// <param name="UnreadableEmailCount">How many carried MIME no reader could parse, which the pass stepped over.</param>
/// <param name="MissingContentEmailCount">How many no longer had raw MIME to re-read.</param>
/// <param name="EmailsRemain">Whether the scope still holds mail a further pass would reach.</param>
internal sealed record MailboxRederivationPass(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("rederivedEmailCount")] int RederivedEmailCount,
    [property: JsonPropertyName("unreadableEmailCount")] int UnreadableEmailCount,
    [property: JsonPropertyName("missingContentEmailCount")] int MissingContentEmailCount,
    [property: JsonPropertyName("emailsRemain")] bool EmailsRemain);
