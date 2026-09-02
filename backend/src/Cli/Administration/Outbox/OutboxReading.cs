// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Cli.Administration.Outbox;

/// <summary>What the fields two outbox readings share mean, written once for both of them.</summary>
/// <remarks>
/// A listing entry and a single send carry the same stage and the same two codes, and each is read the same way
/// whichever answer it arrived in. Stating that here rather than on each record is what keeps the listing and the
/// single reading from printing one send's failure two different ways after somebody changes one of them.
/// </remarks>
internal static class OutboxReading
{
    /// <summary>The stage a send stands at while nobody can say what its recipients received.</summary>
    /// <remarks>
    /// Compared by the deployment's own word rather than by an enumeration of this command's, because the command
    /// prints what the deployment says and the two are versioned separately. A stage this build has never heard of
    /// still prints under its own name.
    /// </remarks>
    internal const string UnknownOutcomeStage = "TransmissionBegun";

    /// <summary>Reports whether a stage is the one that waits for a person rather than for another attempt.</summary>
    /// <param name="stage">The stage the deployment reported, which may be absent or a word this build does not know.</param>
    /// <returns><see langword="true" /> where nobody can say what the send's recipients received.</returns>
    internal static bool StandsAtUnknownOutcome(string? stage) =>
        string.Equals(stage, UnknownOutcomeStage, StringComparison.Ordinal);

    /// <summary>Describes what the last attempt ended in, as the codes an operator looks up.</summary>
    /// <param name="lastFailureCode">The code identifying what the last attempt ended in, absent where the deployment records none.</param>
    /// <param name="lastReplyCode">The reply code the server answered with, absent where it answered none.</param>
    /// <returns>The failure code and the reply code, or a word saying the deployment recorded neither.</returns>
    /// <remarks>
    /// Codes rather than sentences, and by design: a failure a submission server described is that server's text about
    /// somebody's message, and MailFathom's own five-digit code is what the documentation is indexed by.
    /// </remarks>
    internal static string DescribeFailure(int? lastFailureCode, int? lastReplyCode) =>
        (lastFailureCode, lastReplyCode) switch
        {
            (null, null) => "none recorded",
            ({ } failure, null) => string.Create(CultureInfo.InvariantCulture, $"failure {failure}"),
            (null, { } reply) => string.Create(CultureInfo.InvariantCulture, $"reply {reply}"),
            ({ } failure, { } reply) => string.Create(
                CultureInfo.InvariantCulture,
                $"failure {failure}, reply {reply}"),
        };
}
