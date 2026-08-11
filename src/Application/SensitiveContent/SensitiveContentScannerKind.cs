// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent;

/// <summary>Names one of the two scanners a deployment can switch on independently.</summary>
/// <remarks>
/// <para>
/// The two are separated by how precise they are rather than by preference. A provider-prefixed token, a PEM block, or a
/// connection string identifies itself, so <see cref="Secrets" /> is close to exact. Personal data in a mailbox is the
/// opposite: every message carries a name, an address, and a signature block, so <see cref="Pii" /> applied the same way
/// would redact most of a corpus. An operator handling regulated correspondence wants both; the ordinary one wants
/// secrets alone and would switch a single combined switch off entirely rather than accept the second.
/// </para>
/// <para>
/// The member names are the configuration keys under the <c>SensitiveContent</c> section, so a validation message and an
/// operator's file spell a scanner the same way without a translation table between them.
/// </para>
/// </remarks>
public enum SensitiveContentScannerKind
{
    /// <summary>Credentials, tokens, keys, and other machine secrets that identify themselves by their own format.</summary>
    Secrets = 0,

    /// <summary>Personal data beyond the fixed-format identifiers, which needs a language model to recognize.</summary>
    Pii = 1,
}
