// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Administration;

/// <summary>One configuration setting as the deployment actually reads it, with the layer that supplied it.</summary>
/// <param name="Path">The colon-delimited configuration path, spelled as the winning provider holds it.</param>
/// <param name="Value">The value the deployment reads, or the redaction marker where the setting bears a secret.</param>
/// <param name="Source">Which layer of the composed configuration supplied the value.</param>
/// <param name="Origin">What identifies the source within its own kind — a file's path, and nothing for the layers that have only one instance.</param>
/// <param name="IsRedacted">Whether <see cref="Value" /> is the marker rather than what the deployment reads.</param>
/// <remarks>
/// <para>
/// The source is reported beside the value rather than instead of it, because the two answer one question an operator
/// asks before every write: whether persisting this setting will change anything. A value that came from an
/// environment variable will go on coming from there whatever the persisted layer is made to say.
/// </para>
/// <para>
/// <see cref="Origin" /> exists because the file layer is the one whose instances an operator has to tell apart. A
/// value from <c>/etc/mailfathom/config/20-persistence.json</c> and one from the image's own
/// <c>appsettings.json</c> are the same <see cref="SettingSource" /> and are repaired in entirely different places.
/// </para>
/// </remarks>
internal sealed record EffectiveSetting(
    string Path,
    string Value,
    SettingSource Source,
    string? Origin,
    bool IsRedacted);
