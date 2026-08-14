// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>The failure a personal-data analyzer that cannot answer is reported as, for a deployment whose scanner is switched on.</summary>
/// <remarks>
/// <para>
/// Raised by <see cref="IPersonalDataAnalyzerProbe" /> and by nothing on a serving path. The scanner fails closed, so an
/// instance in this state refuses every guarded read, derived write, and egress for as long as it lasts; what the
/// readiness probe does with that is take the instance out of traffic, which is diagnosed at once, while an instance
/// that logged and went on serving is not.
/// </para>
/// <para>
/// <b>No message here carries the analyzer's address</b>, because a message is what reaches a log and
/// <c>src/AGENTS.md</c> § <i>Failures</i> lists a host name beside a credential among the things one may never carry.
/// Each one names <c>SensitiveContent:PersonalDataAnalyzer:Endpoint</c> instead — the key an operator edits to
/// repair any of these three states, and the one thing they need to be told, since the value is already in the file they
/// would open. The resolved address stays on <see cref="Endpoint" /> for a caller with somewhere safe to put it. Nor does
/// a message carry anything the analyzer wrote: the <c>status</c> a refusal is reported with is the caller's own rendering
/// of the status code, because a proxy or a wrong service at that address composes both the body and the reason phrase.
/// </para>
/// </remarks>
public sealed class PersonalDataAnalyzerUnavailableException : MailFathomException
{
    private PersonalDataAnalyzerUnavailableException(string operatorSafeMessage, string endpoint)
        : base(operatorSafeMessage) => this.Endpoint = endpoint;

    private PersonalDataAnalyzerUnavailableException(
        string operatorSafeMessage,
        string endpoint,
        Exception innerException)
        : base(operatorSafeMessage, innerException) => this.Endpoint = endpoint;

    /// <summary>Gets the analyzer address the probe could not get an answer from.</summary>
    public string Endpoint { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.PersonalDataAnalyzerUnavailable;

    /// <summary>Reports that the configured analyzer could not be reached at all.</summary>
    /// <param name="endpoint">The address the probe asked.</param>
    /// <param name="failure">The transport failure, which stays diagnostic detail for a log.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static PersonalDataAnalyzerUnavailableException NotReached(string endpoint, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(failure);

        return new PersonalDataAnalyzerUnavailableException(
            "The personal-data scanner is switched on and the analyzer named by SensitiveContent:PersonalDataAnalyzer:Endpoint could not be reached. Personal-data scanning fails closed, so this instance refuses every read, derived write, and egress it guards and reports unready until the analyzer answers. Start the analyzer beside this service, correct that address, or switch the scanner off.",
            endpoint,
            failure);
    }

    /// <summary>Reports that the analyzer answered the probe with a refusal.</summary>
    /// <param name="endpoint">The address the probe asked.</param>
    /// <param name="status">The status line it answered with.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The answer's body is deliberately not read into the message. It is composed by a service this process does not
    /// own and, on a probe that named a language, may quote the request back.
    /// </remarks>
    public static PersonalDataAnalyzerUnavailableException Refused(string endpoint, string status)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(status);

        return new PersonalDataAnalyzerUnavailableException(
            string.Format(
                CultureInfo.InvariantCulture,
                "The analyzer named by SensitiveContent:PersonalDataAnalyzer:Endpoint answered the personal-data scanner's availability probe with {0}. Check that the address serves a Presidio analyzer rather than another service, and that the language this deployment is configured for is one its own configuration loads a model for.",
                status),
            endpoint);
    }

    /// <summary>Reports that the analyzer detects nothing the switched-on categories map onto.</summary>
    /// <param name="endpoint">The address the probe asked.</param>
    /// <param name="category">The category the analyzer could not answer for.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A category the analyzer recognises no entity of would be scanned for and never found, which is the quiet failure
    /// the whole feature exists to prevent: an operator reading their own configuration would take it as protection that
    /// is in force. It happens when an analyzer runs a recognizer registry of its own rather than the shipped default.
    /// </remarks>
    public static PersonalDataAnalyzerUnavailableException DetectsNothingFor(
        string endpoint,
        SensitiveContentCategory category)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(category);

        return new PersonalDataAnalyzerUnavailableException(
            string.Format(
                CultureInfo.InvariantCulture,
                "The analyzer named by SensitiveContent:PersonalDataAnalyzer:Endpoint recognises no entity the '{0}' category maps onto, so that category would be scanned for and never found. Configure the analyzer's recognizer registry to load it, or leave the category out of the configured list.",
                category),
            endpoint);
    }
}
