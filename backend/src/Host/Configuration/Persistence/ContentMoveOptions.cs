// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.EmailContent.Move;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Bounds the rate at which a deployment carries its already-stored content into the bucket.</summary>
/// <remarks>
/// <para>
/// Nothing here starts a move, and there is no switch that does. A move is an operator's decision taken through the
/// administrative endpoint, because it rewrites where a mailbox is held and a deployment must not begin that on its own
/// the first time it is restarted with a new setting.
/// </para>
/// <para>
/// What these three settle is what the move costs while it runs. A pass carries at most a bounded number of payloads and
/// a bounded volume, whichever it reaches first, and nothing carries the move again until the interval has elapsed — so
/// the deployment spends most of every interval on synchronization, delivery, and the reads a caller is waiting on.
/// Raising the two ceilings or shortening the interval moves the mailbox sooner and leaves less for everything else.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ContentMoveOptions
{
    /// <summary>The configuration path this block is bound from, used to name a faulty setting.</summary>
    internal const string SectionPath = $"{ContentStorageOptions.SectionName}:{nameof(ContentStorageOptions.Move)}";

    /// <summary>The shortest interval a deployment may put between two passes.</summary>
    /// <remarks>Below a second the move stops being background work and becomes a second workload beside the deployment's own.</remarks>
    internal static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    /// <summary>The longest interval a deployment may put between two passes.</summary>
    /// <remarks>An hour between passes over a mailbox of tens of thousands of messages is a move that would not finish in a year, which is a bound nobody meant to set.</remarks>
    internal static readonly TimeSpan MaximumInterval = TimeSpan.FromHours(1);

    private static readonly TimeSpan MinimumLeaseDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinimumLeaseRenewalInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumLeaseRenewalInterval = TimeSpan.FromMinutes(30);

    /// <summary>Gets or sets how long the deployment waits between two bounded passes.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets how many payloads one pass carries before it ends.</summary>
    public int PayloadsPerPass { get; set; } = 20;

    /// <summary>Gets or sets how many bytes of raw MIME one pass carries before it ends, whatever the count says.</summary>
    public long MaxBytesPerPass { get; set; } = 64L * 1024 * 1024;

    /// <summary>Gets or sets how long a replica holds the move from each claim or renewal of its lease.</summary>
    /// <remarks>
    /// <para>
    /// One replica carries the move at a time, holding it for a pass and for the interval after it, so this is the longest
    /// the move waits for another replica after its holder crashed. A replica that stops gracefully gives it back at once.
    /// </para>
    /// <para>
    /// Must be longer than <see cref="LeaseRenewalInterval" />. Half the difference between the two is how long a renewal
    /// may take to be answered, and the other half is how long a pass stopped by a renewal that was not has to record
    /// where it got to before another replica may start one.
    /// </para>
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets how long after the last confirmed claim or renewal the move's lease is renewed.</summary>
    /// <remarks>Only a pass that outlasts it, or an interval longer than it, writes a renewal at all.</remarks>
    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Reports every reason these bounds could not be used, by reading the declaration alone.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the declaration is usable.</returns>
    public IEnumerable<string> FindConfigurationErrors()
    {
        if (RangeError(nameof(this.Interval), this.Interval, MinimumInterval, MaximumInterval) is { } intervalError)
        {
            yield return intervalError;
        }

        if (RangeError(nameof(this.LeaseDuration), this.LeaseDuration, MinimumLeaseDuration, MaximumLeaseDuration) is { } leaseError)
        {
            yield return leaseError;
        }

        if (RangeError(nameof(this.LeaseRenewalInterval), this.LeaseRenewalInterval, MinimumLeaseRenewalInterval, MaximumLeaseRenewalInterval) is { } renewalError)
        {
            yield return renewalError;
        }

        if (this.LeaseRenewalInterval >= this.LeaseDuration)
        {
            yield return Error(
                nameof(this.LeaseRenewalInterval),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is '{0}', which is not shorter than {1}:{2} ('{3}'). A lease renewed no sooner than it expires lets a second replica start a pass while the first is still carrying one.",
                    this.LeaseRenewalInterval,
                    SectionPath,
                    nameof(this.LeaseDuration),
                    this.LeaseDuration));
        }

        if (this.PayloadsPerPass <= 0)
        {
            yield return Error(
                nameof(this.PayloadsPerPass),
                "is not positive. A pass that carries no payload would leave the move running forever without moving anything.");
        }

        if (this.MaxBytesPerPass <= 0)
        {
            yield return Error(
                nameof(this.MaxBytesPerPass),
                "is not positive. A pass ends on whichever ceiling it reaches first, so a ceiling of nothing would end every pass before its first payload.");
        }
    }

    /// <summary>Reads the two keys one bounded pass is bounded by.</summary>
    /// <returns>The bounds the pass ends on.</returns>
    internal StoredContentMoveOptions ToMoveOptions() => new()
    {
        PayloadsPerPass = this.PayloadsPerPass,
        MaxBytesPerPass = this.MaxBytesPerPass,
    };

    private static string? RangeError(string propertyName, TimeSpan value, TimeSpan minimum, TimeSpan maximum) =>
        value < minimum || value > maximum
            ? Error(
                propertyName,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is '{0}', which is outside the permitted range of {1} to {2}.",
                    value,
                    minimum,
                    maximum))
            : null;

    private static string Error(string propertyName, string detail) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1} {2}", SectionPath, propertyName, detail);
}
