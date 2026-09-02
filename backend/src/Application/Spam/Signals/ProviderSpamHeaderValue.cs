// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Signals;

/// <summary>One provider spam header exactly as the message carried it.</summary>
/// <param name="FieldName">The header field name, one of <see cref="ProviderSpamHeaderFields.All" />.</param>
/// <param name="Value">The unfolded header value, uninterpreted.</param>
/// <remarks>
/// The value arrives uninterpreted so that the reading of it happens in one place, where it is unit-testable, rather
/// than inside the MIME adapter. It is untrusted input: a message can carry a header a sending party wrote, which is
/// precisely why a verdict read out of one is recorded with the field it came from instead of as an anonymous score.
/// </remarks>
public sealed record ProviderSpamHeaderValue(string FieldName, string Value);
