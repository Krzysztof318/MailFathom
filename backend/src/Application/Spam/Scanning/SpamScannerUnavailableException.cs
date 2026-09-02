// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Spam.Scanning;

/// <summary>The failure that stops a host whose spam scanner is switched on with nothing answering behind it.</summary>
/// <remarks>
/// <para>
/// Raised by <see cref="ISpamScannerProbe" /> while the host is coming up, and by nothing on a serving path — where a
/// scanner that does not answer is a <see cref="SpamScanOutcome.Unavailable" /> result the classification continues
/// past. The two are deliberately different: one message classified without its second opinion is a weaker record, and
/// a whole deployment classifying without one is a switch that says something untrue.
/// </para>
/// <para>
/// <b>No message here carries the scanner's address</b>, because a message is what reaches a log and
/// <c>backend/src/AGENTS.md</c> § <i>Failures</i> lists a host name beside a credential among the things one may never carry.
/// Each one names <c>SpamClassification:Scanner:Host</c> instead, which is the key an operator edits, and the resolved
/// address stays on <see cref="Endpoint" /> for a caller with somewhere safe to put it.
/// </para>
/// </remarks>
public sealed class SpamScannerUnavailableException : MailFathomException
{
    private SpamScannerUnavailableException(string operatorSafeMessage, string endpoint)
        : base(operatorSafeMessage) => this.Endpoint = endpoint;

    private SpamScannerUnavailableException(string operatorSafeMessage, string endpoint, Exception innerException)
        : base(operatorSafeMessage, innerException) => this.Endpoint = endpoint;

    /// <summary>Gets the scanner address the probe could not get an answer from.</summary>
    public string Endpoint { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.SpamScannerUnavailable;

    /// <summary>Refuses to start because the configured scanner could not be reached at all.</summary>
    /// <param name="endpoint">The address the probe asked.</param>
    /// <param name="failure">The transport failure, which stays diagnostic detail for a log.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static SpamScannerUnavailableException NotReached(string endpoint, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(failure);

        return new SpamScannerUnavailableException(
            "The spam scanner is switched on and the daemon named by SpamClassification:Scanner:Host could not be reached. Every message would then be classified from its headers alone while the configuration said a scanner was consulted. Deploy the scanner beside this service, correct that address, or set SpamClassification:UseScanner to false.",
            endpoint,
            failure);
    }

    /// <summary>Refuses to start because something answered at that address without speaking the scanner's protocol.</summary>
    /// <param name="endpoint">The address the probe asked.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// What it answered with is deliberately not quoted. The bytes are composed by a service this process does not own,
    /// and a proxy or a wrong service at that address answers with whatever it likes — including, on a port somebody
    /// pointed at the wrong container, a fragment of somebody else's traffic.
    /// </remarks>
    public static SpamScannerUnavailableException NotASpamDaemon(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return new SpamScannerUnavailableException(
            "Something answered at the address in SpamClassification:Scanner:Host without speaking the spam daemon's protocol. Check that the address and port reach a spamd rather than another service.",
            endpoint);
    }
}
