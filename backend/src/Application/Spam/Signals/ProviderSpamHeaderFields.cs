// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Signals;

/// <summary>The header fields a provider's own spam verdict is read from.</summary>
/// <remarks>
/// <para>
/// The set is deliberately narrow. These four fields have documented, stable meanings — SpamAssassin defined them and
/// the mail servers that score before delivery write them in the same shape — so a value read from one of them means
/// what this system takes it to mean. A provider-specific report field such as an antispam confidence level is a
/// different grammar per vendor, and reading a verdict out of one on a guess would file somebody's mail on a
/// misunderstanding.
/// </para>
/// <para>
/// The set is shared between the adapter that selects headers out of raw MIME and the stage that interprets them, so
/// neither can come to recognize a field the other does not.
/// </para>
/// </remarks>
public static class ProviderSpamHeaderFields
{
    /// <summary>The field carrying a plain yes-or-no verdict, written as <c>YES</c> or <c>NO</c>.</summary>
    public const string SpamFlag = "X-Spam-Flag";

    /// <summary>The field carrying the verdict together with the score and the threshold it was judged against.</summary>
    /// <remarks>
    /// Its shape is <c>Yes, score=15.2 required=5.0 tests=...</c>, which is the one deterministic source of both numbers
    /// in the same scale. That is why an assessment is only recorded when this field carries both of them.
    /// </remarks>
    public const string SpamStatus = "X-Spam-Status";

    /// <summary>The field carrying the score alone.</summary>
    public const string SpamScore = "X-Spam-Score";

    /// <summary>The field carrying the score as a run of asterisks, one per whole point.</summary>
    public const string SpamLevel = "X-Spam-Level";

    /// <summary>Gets every field a provider verdict is read from.</summary>
    public static IReadOnlyList<string> All { get; } = [SpamFlag, SpamStatus, SpamScore, SpamLevel];

    /// <summary>Reports whether a header field carries a provider's spam verdict.</summary>
    /// <param name="fieldName">The header field name, in any case.</param>
    /// <returns><see langword="true" /> when the field is one this system reads a verdict from.</returns>
    /// <remarks>
    /// Compared without regard to case, because a header field name is case-insensitive by RFC 5322 and mail servers
    /// write these four in several casings.
    /// </remarks>
    public static bool IsRecognized(string? fieldName) =>
        fieldName is not null && All.Contains(fieldName.Trim(), StringComparer.OrdinalIgnoreCase);
}
