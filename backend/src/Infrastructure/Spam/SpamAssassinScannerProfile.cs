// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Infrastructure.Spam;

/// <summary>Where the spam daemon is and what a call to it is allowed to cost.</summary>
/// <remarks>
/// <para>
/// The daemon is <b>expected to be deployment-local</b>. Scanning is the one path that hands a whole message to a
/// separate process, and what makes that acceptable is that the process is inside the same trust boundary; an address on
/// the public internet sends the owner's mail, in full and unredacted, to somebody else in order to find out whether it
/// is spam. Nothing here refuses one, because a deployment may legitimately run one daemon for several services on its
/// own network and no rule about addresses tells the two apart, and the documentation states what is given up.
/// </para>
/// <para>
/// All three bounds are this adapter's rather than the caller's, because each is a property of the daemon rather than of
/// classification: the size limit is what a rule corpus can usefully read, the timeout is how long one scan may take,
/// and the concurrency is matched to the daemon's own child limit. A message that exceeds any of them leaves the
/// classification with its deterministic verdict.
/// </para>
/// </remarks>
public sealed record SpamAssassinScannerProfile
{
    /// <summary>The port a deployment that named none reaches the daemon on.</summary>
    /// <remarks>The port SpamAssassin's own daemon listens on by default and every client of it assumes.</remarks>
    public const int DefaultPort = 783;

    private SpamAssassinScannerProfile(
        string host,
        int port,
        TimeSpan scanTimeout,
        int maximumMessageBytes,
        int maximumConcurrentScans)
    {
        this.Host = host;
        this.Port = port;
        this.ScanTimeout = scanTimeout;
        this.MaximumMessageBytes = maximumMessageBytes;
        this.MaximumConcurrentScans = maximumConcurrentScans;
    }

    /// <summary>Gets the daemon's host name or address.</summary>
    public string Host { get; }

    /// <summary>Gets the TCP port it listens on.</summary>
    public int Port { get; }

    /// <summary>Gets how long one exchange may take before it is abandoned.</summary>
    public TimeSpan ScanTimeout { get; }

    /// <summary>Gets the largest message that is sent to the daemon at all.</summary>
    public int MaximumMessageBytes { get; }

    /// <summary>Gets how many exchanges may be in flight at once.</summary>
    public int MaximumConcurrentScans { get; }

    /// <summary>Gets the address this profile reaches, for a caller with somewhere safe to record it.</summary>
    /// <remarks>Composed on demand rather than stored, so nothing carries it into a message by reaching for the nearest property.</remarks>
    public string Endpoint => string.Format(CultureInfo.InvariantCulture, "{0}:{1}", this.Host, this.Port);

    /// <summary>Composes the profile a configured daemon is reached under.</summary>
    /// <param name="host">The daemon's host name or address.</param>
    /// <param name="port">The TCP port it listens on.</param>
    /// <param name="scanTimeout">How long one exchange may take.</param>
    /// <param name="maximumMessageBytes">The largest message that is sent at all.</param>
    /// <param name="maximumConcurrentScans">How many exchanges may be in flight at once.</param>
    /// <returns>The validated profile.</returns>
    /// <exception cref="ArgumentException">Thrown when the host is blank or is neither a host name nor an IP address.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the port is outside the TCP range, or a bound is not positive.</exception>
    /// <remarks>
    /// A bad host is refused rather than repaired, and the refusal quotes no part of it, for the reason
    /// <c>backend/src/AGENTS.md</c> § <i>Failures</i> gives: this message reaches a startup log and a host name never does. What
    /// it names instead is the shape of a good value, which is what an operator with the file already open needs.
    /// </remarks>
    public static SpamAssassinScannerProfile Create(
        string host,
        int port,
        TimeSpan scanTimeout,
        int maximumMessageBytes,
        int maximumConcurrentScans)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        // A host name, an IPv4 address, and an IPv6 address are the three things a socket can be opened against, and
        // the framework already knows which is which. Everything an operator writes instead — a URL, a host with the
        // port stuck on it, two words — comes back as Unknown, which is the whole refusal in one call.
        if (Uri.CheckHostName(host.Trim()) is UriHostNameType.Unknown)
        {
            throw new ArgumentException(
                "That is not an address the spam daemon can be reached at. State the host name or IP address on its own, such as mailfathom-spamassassin, and put the port in its own setting.",
                nameof(host));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "A TCP port is between 1 and 65535.");
        }

        if (scanTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanTimeout),
                scanTimeout,
                "A scan timeout is a positive interval.");
        }

        if (maximumMessageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumMessageBytes),
                maximumMessageBytes,
                "The largest message a scanner is sent is a positive number of bytes.");
        }

        if (maximumConcurrentScans <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumConcurrentScans),
                maximumConcurrentScans,
                "At least one scan has to be allowed to run at a time.");
        }

        return new SpamAssassinScannerProfile(
            host.Trim(),
            port,
            scanTimeout,
            maximumMessageBytes,
            maximumConcurrentScans);
    }
}
