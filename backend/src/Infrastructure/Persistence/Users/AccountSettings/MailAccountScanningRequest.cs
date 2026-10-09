// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>One distinct answer accounts gave about scanning their mail, however many of them gave it.</summary>
/// <param name="ScansFor">The scanners the accounts switched on for themselves.</param>
/// <param name="ScreensOutgoingMailFor">The scanners whose findings stop a message they send.</param>
public sealed record MailAccountScanningRequest(
    IReadOnlyList<SensitiveContentScannerKind> ScansFor,
    IReadOnlyList<SensitiveContentScannerKind> ScreensOutgoingMailFor);
