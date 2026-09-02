// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>One method's outcome as a receiving server stated it, uninterpreted.</summary>
/// <param name="Method">The method RFC 8601 names, such as <c>dkim</c>, <c>spf</c>, or <c>dmarc</c>.</param>
/// <param name="Result">The outcome that method reached, such as <c>pass</c> or <c>fail</c>.</param>
/// <param name="Properties">The properties written beside the outcome, in the order the header wrote them.</param>
/// <remarks>
/// Both text members are compared without regard to case, because RFC 8601 defines the method and result tokens as
/// case-insensitive and servers write them both ways.
/// </remarks>
public sealed record ReportedAuthenticationMethod(
    string Method,
    string Result,
    IReadOnlyList<ReportedAuthenticationProperty> Properties);
