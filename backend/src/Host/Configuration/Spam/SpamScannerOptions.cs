// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Infrastructure.Spam;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Where the spam daemon is and what one scan of a message may cost.</summary>
/// <remarks>
/// <para>
/// A block of its own under the classification section, because it describes a container rather than a decision about
/// mail: whether a scanner is consulted is <c>UseScanner</c> beside it, and everything here is only read once that is
/// on. A deployment that never switched the scanner on may leave an address here or leave it out, and neither is a
/// failure, because nothing constructs a daemon conversation for it.
/// </para>
/// <para>
/// The host and the port are two settings rather than one address, because a spam daemon speaks a line protocol on a
/// TCP port rather than HTTP on a URL. There is no scheme to state and no path to resolve, so an address written as one
/// would be a shape this adapter would have to take apart again.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SpamScannerOptions
{
    /// <summary>The shortest scan budget an operator may configure.</summary>
    /// <remarks>A whole-message scan runs a rule corpus, so anything below this expires before the daemon could answer and would switch the scanner off by timing out on every message.</remarks>
    internal const int SmallestScanTimeoutSeconds = 1;

    /// <summary>The budget a deployment that states none receives.</summary>
    /// <remarks>
    /// Long enough for a cold daemon to run a full corpus over a large message and short enough that a wedged one is
    /// noticed within one message rather than one run.
    /// </remarks>
    internal const int DefaultScanTimeoutSeconds = 30;

    /// <summary>The longest scan budget an operator may configure.</summary>
    /// <remarks>
    /// Well inside the five minutes the daemon's own child timeout defaults to, and far above what a scan takes. A
    /// budget beyond it would let one message hold a classification run for longer than the run's own pacing, which is
    /// a way to stall a synchronization rather than a way to scan a large message.
    /// </remarks>
    internal const int LargestScanTimeoutSeconds = 120;

    /// <summary>The largest message that is sent when an operator states nothing.</summary>
    /// <remarks>
    /// The size SpamAssassin's own client truncates at, which is the number the corpus was tuned against: rules read
    /// headers and the readable part of a body, and a megabyte of base64 attachment past that changes no score. It is
    /// also the bound that keeps one oversized message from being copied into a socket buffer on every scan.
    /// </remarks>
    internal const int DefaultMaximumMessageBytes = 512_000;

    /// <summary>The smallest size limit an operator may configure.</summary>
    /// <remarks>Below this a limit would refuse ordinary correspondence, which reads as a scanner that has stopped working rather than as a limit doing its job.</remarks>
    internal const int SmallestMaximumMessageBytes = 32_000;

    /// <summary>The largest size limit an operator may configure.</summary>
    /// <remarks>An answer well past the point where more bytes stop changing a score, kept finite so the bound is a bound.</remarks>
    internal const int LargestMaximumMessageBytes = 32 * 1024 * 1024;

    /// <summary>How many scans run at once when an operator states nothing.</summary>
    /// <remarks>
    /// The number of children the daemon spawns by default. Sending more at once does not scan more at once — it queues
    /// them inside the daemon, where this deployment's own timeout cannot see the wait — so the two numbers are one
    /// decision, and an operator who raises the daemon's child limit raises this with it.
    /// </remarks>
    internal const int DefaultMaximumConcurrentScans = 5;

    /// <summary>The largest concurrency an operator may configure.</summary>
    /// <remarks>Far above any daemon's child limit, and finite so that a mistyped value cannot remove the bound.</remarks>
    internal const int LargestMaximumConcurrentScans = 64;

    /// <summary>Gets or sets the daemon's host name or address.</summary>
    /// <remarks>
    /// Required once <c>UseScanner</c> is on, and refused at startup when it is absent: a scanner switched on with
    /// nowhere to ask would classify every message from its headers alone while the configuration said otherwise.
    /// </remarks>
    public string? Host { get; set; }

    /// <summary>Gets or sets the TCP port the daemon listens on.</summary>
    public int Port { get; set; } = SpamAssassinScannerProfile.DefaultPort;

    /// <summary>Gets or sets how many seconds one scan may take before the message keeps the verdict its headers reached.</summary>
    public int ScanTimeoutSeconds { get; set; } = DefaultScanTimeoutSeconds;

    /// <summary>Gets or sets the largest message that is sent to the daemon at all.</summary>
    /// <remarks>A message beyond it is left with the verdict its headers reached, which is a property of the message rather than of the deployment: retrying it produces the same answer.</remarks>
    public int MaximumMessageBytes { get; set; } = DefaultMaximumMessageBytes;

    /// <summary>Gets or sets how many scans this deployment runs at once.</summary>
    public int MaximumConcurrentScans { get; set; } = DefaultMaximumConcurrentScans;

    /// <summary>Finds everything about this block that would otherwise be discovered on somebody's mail.</summary>
    /// <param name="useScanner">Whether the classification section switched the scanner on.</param>
    /// <returns>One result per mistake, each naming the key that carries it.</returns>
    internal IEnumerable<ValidationResult> FindErrors(bool useScanner)
    {
        if (useScanner && string.IsNullOrWhiteSpace(this.Host))
        {
            yield return new ValidationResult(
                $"{SpamClassificationOptions.SectionName} switches the scanner on and names no {nameof(this.Host)} for it. State the host name or address of the spam daemon deployed beside this service, such as mailfathom-spamassassin.",
                [nameof(this.Host)]);
        }

        if (this.Port is < 1 or > 65535)
        {
            yield return new ValidationResult(
                $"{SpamClassificationOptions.SectionName} declares a scanner {nameof(this.Port)} of {this.Port.ToString(CultureInfo.InvariantCulture)}, and a TCP port is between 1 and 65535.",
                [nameof(this.Port)]);
        }

        if (this.ScanTimeoutSeconds is < SmallestScanTimeoutSeconds or > LargestScanTimeoutSeconds)
        {
            yield return new ValidationResult(
                $"{SpamClassificationOptions.SectionName} declares a scanner {nameof(this.ScanTimeoutSeconds)} of {this.ScanTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}, and a scan budget is between {SmallestScanTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} and {LargestScanTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                [nameof(this.ScanTimeoutSeconds)]);
        }

        if (this.MaximumMessageBytes is < SmallestMaximumMessageBytes or > LargestMaximumMessageBytes)
        {
            yield return new ValidationResult(
                $"{SpamClassificationOptions.SectionName} declares a scanner {nameof(this.MaximumMessageBytes)} of {this.MaximumMessageBytes.ToString(CultureInfo.InvariantCulture)}, and the largest message a scanner is sent is between {SmallestMaximumMessageBytes.ToString(CultureInfo.InvariantCulture)} and {LargestMaximumMessageBytes.ToString(CultureInfo.InvariantCulture)} bytes.",
                [nameof(this.MaximumMessageBytes)]);
        }

        if (this.MaximumConcurrentScans is < 1 or > LargestMaximumConcurrentScans)
        {
            yield return new ValidationResult(
                $"{SpamClassificationOptions.SectionName} declares a scanner {nameof(this.MaximumConcurrentScans)} of {this.MaximumConcurrentScans.ToString(CultureInfo.InvariantCulture)}, and between 1 and {LargestMaximumConcurrentScans.ToString(CultureInfo.InvariantCulture)} scans may run at once.",
                [nameof(this.MaximumConcurrentScans)]);
        }
    }

    /// <summary>Composes the profile the adapter reaches the daemon under.</summary>
    /// <returns>The validated profile.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the scanner was switched on with no host, which validation refuses before anything resolves this.</exception>
    internal SpamAssassinScannerProfile ToProfile() => SpamAssassinScannerProfile.Create(
        this.Host ?? throw new InvalidOperationException(
            $"The spam scanner was switched on at registration and {SpamClassificationOptions.SectionName} names no scanner host in the validated configuration."),
        this.Port,
        TimeSpan.FromSeconds(this.ScanTimeoutSeconds),
        this.MaximumMessageBytes,
        this.MaximumConcurrentScans);
}
