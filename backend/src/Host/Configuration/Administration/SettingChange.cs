// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Administration;

/// <summary>What one setting read as before a committed write and what it reads as after it.</summary>
/// <param name="Path">The colon-delimited configuration path the write named.</param>
/// <param name="Before">What the deployment read at the path before the commit, or <see langword="null" /> where no source supplied it.</param>
/// <param name="After">What the deployment reads at the path now, or <see langword="null" /> where the write left nothing supplying it.</param>
/// <remarks>
/// <para>
/// Both halves are the <em>effective</em> value rather than the persisted one, which is the whole reason the pair is
/// reported. A write that persisted a value beneath an environment variable leaves the two identical, and that
/// identity is what tells an operator their change reached the document and not the deployment.
/// </para>
/// <para>
/// An absent half is a real answer rather than a missing one: before a first write nothing supplied the path, and
/// after a removal nothing supplies it again unless a source beneath the layer does — which is exactly what an
/// operator asking for the setting to be inherited again wants to see.
/// </para>
/// </remarks>
internal sealed record SettingChange(string Path, EffectiveSetting? Before, EffectiveSetting? After);
