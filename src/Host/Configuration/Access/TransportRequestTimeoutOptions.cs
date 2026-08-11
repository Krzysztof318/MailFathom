// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Access;

/// <summary>How long one request may occupy an endpoint before it is abandoned.</summary>
/// <remarks>
/// <para>
/// This bounds a different resource from <see cref="TransportRateLimitingOptions" />, which is why it is a section of
/// its own rather than another number inside that one. Rate limiting decides how much traffic is admitted; this decides
/// how long an admitted request may hold what it was admitted with. A concurrency permit is taken on the way in and
/// released when the request ends, so without a ceiling here the permit count bounds how many requests run at once and
/// nothing at all bounds for how long — twenty requests that never finish take a surface out of service without
/// exceeding any rate.
/// </para>
/// <para>
/// The two are configured independently for the deployment that has one and not the other. An ingress that shapes
/// arrival rates does not necessarily abandon a request its backend is still holding, and one that enforces its own
/// request deadline may leave the arrival rate to this process; either operator turns off the half they already have
/// without giving up the other.
/// </para>
/// <para>
/// It is a bound on a hang rather than a guarantee that no legitimate request is ever abandoned, and the difference is
/// worth stating because the two cannot both hold here. An <c>ask_mail</c> run is a conversation whose length the model
/// decides, bounded by <c>MailAnswering:MaxProviderCallsPerRun</c> at eight calls, and every one of them is an
/// <c>AiProviderInvocation</c> whose own total timeout is five minutes. A ceiling that enclosed the maximum would have
/// to be past half an hour, which is not a request ceiling at all and would let one stalled run hold a concurrency
/// permit for that long. <see cref="DefaultDuration" /> states which way that is resolved.
/// </para>
/// <para>
/// The section is read once, while the host is being composed, because the policy is attached to a route as the
/// application is built. A change takes effect on restart.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class TransportRequestTimeoutOptions
{
    /// <summary>The longest ceiling an operator may state.</summary>
    /// <remarks>An hour is past the point where a request is a request rather than a job, and a ceiling beyond it would be indistinguishable from having none while still reading as a bound.</remarks>
    private static readonly TimeSpan LongestDuration = TimeSpan.FromHours(1);

    /// <summary>The shortest ceiling an operator may state.</summary>
    /// <remarks>Below a second, an ordinary request against a warm local database would be abandoned as often as it was served, which is an outage configured by hand rather than a bound.</remarks>
    private static readonly TimeSpan ShortestDuration = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets whether the endpoint abandons a request that outlives <see cref="Duration" />.</summary>
    /// <remarks>On unless a deployment states otherwise, so an endpoint somebody enabled cannot hold a permit indefinitely because nobody wrote a number. Turning it off is the right setting only where something in front of this process already abandons a request its backend is still serving.</remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets how long one request may run before it is abandoned.</summary>
    /// <remarks>
    /// Ten minutes, which is chosen against what a request costs to hold rather than against what the slowest one may
    /// legitimately spend. It clears an ordinary answering run by a wide margin and is deliberately below the
    /// three-quarters of an hour a maximal one could reach, so a run that walks its whole provider budget is abandoned:
    /// that is the trade taken, because a ceiling sized for the maximum would leave twenty permits holdable for longer
    /// than any caller waits. An operator who raises <c>MailAnswering:MaxProviderCallsPerRun</c>, or whose questions
    /// genuinely run long, raises this with it. A deployment serving no AI-backed tool narrows it instead — every other
    /// MCP tool answers from the local mailbox copy with a bounded query, so a minute is generous there.
    /// </remarks>
    public TimeSpan Duration { get; set; } = DefaultDuration;

    /// <summary>The ceiling a deployment that configures nothing runs under.</summary>
    private static TimeSpan DefaultDuration { get; } = TimeSpan.FromMinutes(10);

    /// <summary>Finds everything an operator must fix before the ceiling can be applied.</summary>
    /// <returns>One message per faulty setting, relative to this section, empty when the ceiling is usable.</returns>
    /// <remarks>Produced in one pass, so an operator reads every mistake at once rather than the first one to be reached.</remarks>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        if (!this.Enabled)
        {
            return [];
        }

        if (this.Duration < ShortestDuration || this.Duration > LongestDuration)
        {
            return
            [
                $"{nameof(this.Duration)} — '{this.Duration}' is outside {ShortestDuration} to {LongestDuration}; a shorter ceiling would abandon an ordinary request as often as it served one, and a longer one would leave a stalled request holding its concurrency permit for longer than any caller would wait.",
            ];
        }

        return [];
    }
}
