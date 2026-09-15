// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Export;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Configures what an export of a mailbox may carry, and how long the archive it produces is kept.</summary>
/// <remarks>
/// <para>
/// A section of its own rather than a block inside <c>ContentStorage</c>, because these are decisions about a mailbox
/// leaving the deployment rather than about where the deployment keeps mail. An operator raising a size limit is
/// answering "how large a mailbox may somebody take away in one piece"; an operator choosing where content is stored is
/// answering something else entirely, and the two are changed by different people at different times.
/// </para>
/// <para>
/// Both bounds cost storage rather than processor time. An archive is a second full copy of a mailbox for as long as it
/// is kept, so the retention period is how long this deployment holds that mailbox twice — the operator page says so
/// beside the backup obligation a drained account carries.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailboxExportOptions
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "MailboxExport";

    /// <summary>Gets or sets the most stored mail one export may carry.</summary>
    /// <remarks>
    /// Measured against the stored mail rather than against the archive, because the first is knowable before any work
    /// happens and the second is not. The floor is a mebibyte, which is small enough to refuse everything in practice
    /// and is there so a deployment can turn the feature down to nothing without a second setting for it.
    /// </remarks>
    [Range(1L * 1024 * 1024, 1024L * 1024 * 1024 * 1024)]
    public long MaximumArchiveByteLength { get; set; } = MailboxExportSettings.DefaultMaximumArchiveByteLength;

    /// <summary>Gets or sets how long a finished archive stays downloadable before the deployment deletes it.</summary>
    /// <remarks>
    /// The default is two days, which is long enough for somebody told their export is ready to fetch it and short
    /// enough that a deployment is not quietly storing every mailbox twice. Raising it raises the storage this
    /// deployment holds, and an operator who wants an archive gone sooner deletes it rather than lowering this.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:05:00", "7.00:00:00")]
    public TimeSpan Retention { get; set; } = MailboxExportSettings.DefaultRetention;

    /// <summary>Gets or sets how often this deployment looks for archives whose retention has run out.</summary>
    /// <remarks>
    /// It is how late an expiry may be rather than when it happens: the pass reads what each export recorded when it
    /// finished, so an interval only decides how soon after the period ends the object actually goes. One replica runs
    /// a pass at a time, under the sweep's own lease.
    /// </remarks>
    /// <remarks>
    /// The upper bound is written as <c>1.00:00:00</c> rather than <c>24:00:00</c>, because the second of those is a
    /// day count to <see cref="TimeSpan" />'s own parser: <c>TimeSpan.Parse("24:00:00")</c> answers twenty-four days,
    /// so a bound written that way would admit an interval that sweeps three times a season.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan ExpirySweepInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Reads the two bounds an export is taken under.</summary>
    /// <returns>The bounds the use case refuses an export against.</returns>
    internal MailboxExportSettings ToExportSettings() => new(this.MaximumArchiveByteLength, this.Retention);
}
