// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Export;

/// <summary>What bounds an export: how large an archive this deployment will produce, and how long it keeps one.</summary>
/// <param name="MaximumArchiveByteLength">The most stored mail one export may carry, measured before a job exists and enforced again while the archive is written.</param>
/// <param name="Retention">How long a finished archive stays downloadable before the deployment deletes it.</param>
/// <remarks>
/// <para>
/// Both are the operator's, because both are about their storage rather than about anybody's mailbox. An archive is a
/// second full copy of a mailbox for as long as it is kept, so the retention period is the length of time the
/// deployment's storage holds that mailbox twice — which is why the default is short enough to download within and why
/// the operator documentation says so.
/// </para>
/// <para>
/// The size limit is measured against the stored mail an export would carry rather than against the archive it
/// produces, because the first is knowable before any work happens and the second is not.
/// </para>
/// </remarks>
public sealed record MailboxExportSettings(long MaximumArchiveByteLength, TimeSpan Retention)
{
    /// <summary>The retention period a deployment that configured none keeps an archive for.</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(48);

    /// <summary>The size limit a deployment that configured none exports under, which is a mailbox far larger than most.</summary>
    public const long DefaultMaximumArchiveByteLength = 64L * 1024 * 1024 * 1024;

    /// <summary>Gets the settings a deployment that configured none exports under.</summary>
    public static MailboxExportSettings Default { get; } =
        new(DefaultMaximumArchiveByteLength, DefaultRetention);
}
