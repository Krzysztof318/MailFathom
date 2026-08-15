// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>One property a receiving server wrote beside a method's result, uninterpreted.</summary>
/// <param name="Type">The <c>ptype</c> RFC 8601 names the property under, such as <c>header</c> or <c>smtp</c>.</param>
/// <param name="Name">The property within that type, such as <c>d</c> for a DKIM signing domain.</param>
/// <param name="Value">The value the server wrote, exactly as it wrote it.</param>
/// <remarks>
/// This is where the identity actually lives: a result of <c>pass</c> says nothing on its own, and the property beside
/// it is what says whose domain passed. It is carried across the parsing boundary uninterpreted so that deciding which
/// property means what happens where it is unit-testable rather than inside a MIME library's adapter.
/// <para>
/// <paramref name="Value" /> names a domain or a mailbox, so it is personal data and is never logged.
/// </para>
/// </remarks>
public sealed record ReportedAuthenticationProperty(string Type, string Name, string Value);
