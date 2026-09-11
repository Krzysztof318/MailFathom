// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Observability.ClientTelemetry;

/// <summary>The least severe log record a deployment asks its clients to record, which is what decides how much they say.</summary>
/// <remarks>
/// <para>
/// The client's own log vocabulary reaches down to <see cref="Trace" />, where a record per request and a record per
/// screen is exactly the point; a deployment serving many signed-in clients wants none of that in the ordinary case, and
/// the client refuses a record below this floor before it is written rather than filtering it on the way out. So the
/// volume is a deployment's decision rather than a client's, and it is taken in the one place that can see how many
/// clients there are.
/// </para>
/// <para>
/// The members are OpenTelemetry's own severity names, because that is what the records carry: a floor spelled in a
/// vocabulary of this repository's own would have to be mapped to a severity number anyway, and the mapping is where the
/// two spellings would drift. There is deliberately no member meaning <c>off</c> — whether a client exports at all is
/// decided by whether the deployment named a collector, and a second key able to say no would be a way for the two to
/// disagree about the same question.
/// </para>
/// </remarks>
internal enum ClientTelemetryLevel
{
    /// <summary>Everything the client records, including one record per request it makes and one per move between screens.</summary>
    Trace = 0,

    /// <summary>The client's account of what it did — signing in, reconnecting, synchronizing, and the acts a deployment refused — without the per-request stream above it.</summary>
    Debug = 1,

    /// <summary>The default, and one record per signed-in session: what a collector keeping everything at its own default level would otherwise be filled with.</summary>
    Info = 2,

    /// <summary>Only what an operator would act on, such as a credential a deployment stopped accepting.</summary>
    Warn = 3,

    /// <summary>Only a failure, such as a region of the client that threw while it was being drawn.</summary>
    Error = 4,

    /// <summary>Only a client nobody can use, which is the containment boundary around the whole application having failed.</summary>
    Fatal = 5,
}

/// <summary>How a <see cref="ClientTelemetryLevel" /> is spelled where a client reads it.</summary>
internal static class ClientTelemetryLevels
{
    /// <summary>Names the level on the wire, in the spelling the client parses.</summary>
    /// <param name="level">The configured floor.</param>
    /// <returns>The published name.</returns>
    /// <remarks>
    /// Written out rather than derived from the member name, which is what makes the published vocabulary one readable
    /// list and stops a rename here from silently becoming a contract change. It is also the only form the analyzers
    /// allow: lowercasing an identifier reads as normalizing one, and this is a spelling rather than a normalization.
    /// </remarks>
    internal static string Published(this ClientTelemetryLevel level) => level switch
    {
        ClientTelemetryLevel.Trace => "trace",
        ClientTelemetryLevel.Debug => "debug",
        ClientTelemetryLevel.Info => "info",
        ClientTelemetryLevel.Warn => "warn",
        ClientTelemetryLevel.Error => "error",
        ClientTelemetryLevel.Fatal => "fatal",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "The level is not one this deployment publishes."),
    };
}
