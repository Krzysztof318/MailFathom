// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>The failure that stops a host whose personal-data scanner is switched on with no analyzer behind it.</summary>
/// <remarks>
/// <para>
/// Raised by <see cref="IPersonalDataAnalyzerProbe" /> while the host is coming up, and by nothing on a serving path.
/// The scanner fails closed, so a deployment reaching this state would refuse every guarded read, derived write, and
/// egress for as long as it ran; refusing to start instead is diagnosed at once, while an instance that logged and
/// carried on is not.
/// </para>
/// <para>
/// <b>This is the one sensitive-content failure that names an address.</b>
/// <see cref="SensitiveContentScannerUnavailableException" /> names no endpoint because nothing a caller does with a
/// refused scan depends on one. Here the address is the whole content of the message: it is the deployment's own
/// configured analyzer rather than a remote party's server, an operator cannot repair what they are not told, and the
/// value is already in the configuration file they would edit.
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

    /// <summary>Refuses to start because the configured analyzer could not be reached at all.</summary>
    /// <param name="endpoint">The address the probe asked.</param>
    /// <param name="failure">The transport failure, which stays diagnostic detail for a log.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static PersonalDataAnalyzerUnavailableException NotReached(string endpoint, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(failure);

        return new PersonalDataAnalyzerUnavailableException(
            string.Format(
                CultureInfo.InvariantCulture,
                "The personal-data scanner is switched on and the analyzer at {0} could not be reached. Personal-data scanning fails closed, so this deployment would refuse every read, derived write, and egress it guards. Start the analyzer beside this service, correct SensitiveContent:PersonalDataAnalyzer:Endpoint, or switch the scanner off.",
                endpoint),
            endpoint,
            failure);
    }

    /// <summary>Refuses to start because the analyzer answered the probe with a refusal.</summary>
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
                "The analyzer at {0} answered the personal-data scanner's startup probe with {1}. Check that the address serves a Presidio analyzer rather than another service, and that the language this deployment is configured for is one its own configuration loads a model for.",
                endpoint,
                status),
            endpoint);
    }

    /// <summary>Refuses to start because the analyzer detects nothing the switched-on categories map onto.</summary>
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
                "The analyzer at {0} recognises no entity the '{1}' category maps onto, so that category would be scanned for and never found. Configure the analyzer's recognizer registry to load it, or leave the category out of the configured list.",
                endpoint,
                category),
            endpoint);
    }
}
