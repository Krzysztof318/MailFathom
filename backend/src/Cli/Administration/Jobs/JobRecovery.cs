// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Jobs;

/// <summary>What a deployment is asked when one dead letter is to be run again or dropped.</summary>
/// <param name="Job">The identifier the dead-letter reading reports for the job.</param>
internal sealed record JobRecoveryRequest([property: JsonPropertyName("job")] Guid Job);

/// <summary>What became of a job the operator decided about.</summary>
/// <param name="Job">The job the decision named.</param>
/// <param name="Outcome">What happened, as the deployment names it.</param>
internal sealed record JobRecovery(
    [property: JsonPropertyName("job")] Guid Job,
    [property: JsonPropertyName("outcome")] string? Outcome)
{
    /// <summary>The outcome a deployment reports when the decision took effect.</summary>
    internal const string AcceptedOutcome = "Accepted";

    /// <summary>The outcome a deployment reports when it holds no job with the identifier named.</summary>
    internal const string JobUnknownOutcome = "JobUnknown";

    /// <summary>Gets whether the decision was the one that took effect.</summary>
    internal bool WasAccepted => string.Equals(this.Outcome, AcceptedOutcome, StringComparison.Ordinal);

    /// <summary>States what a decision that did not take effect means, in terms of what the operator does next.</summary>
    /// <returns>The sentence to print.</returns>
    /// <remarks>
    /// Both refusals are ordinary rather than exceptional, which is why neither is a failure of the command. A job the
    /// deployment does not hold is most often an identifier from another deployment or a pruned row; a job that is no
    /// longer dead-lettered is what a second terminal, or a list a few minutes old, produces.
    /// </remarks>
    internal string DescribeRefusal() => string.Equals(this.Outcome, JobUnknownOutcome, StringComparison.Ordinal)
        ? $"The deployment holds no job {this.Job:D}. Read the dead letters again: the identifier may belong to another deployment."
        : $"Job {this.Job:D} is no longer dead-lettered, so nothing was changed. Something else has already retried or dropped it.";
}
