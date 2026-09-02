// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Common.Observability;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Makes MailFathom's own spans exist for the duration of a test, so nesting can be observed at all.</summary>
/// <remarks>
/// An activity source with nothing listening to it starts no activity: <c>StartActivity</c> returns
/// <see langword="null" />, every tag written to it goes nowhere, and <see cref="Activity.Current" /> stays whatever it
/// already was. A test asserting that work ran inside a span would therefore pass or fail on whether a listener
/// happened to exist rather than on what the code under test does — and would report a worker that opened no span at
/// all as indistinguishable from one that did. This is the listener that removes that ambiguity.
/// </remarks>
internal static class SampledMailFathomSpans
{
    /// <summary>Samples every span MailFathom publishes until the returned listener is disposed.</summary>
    /// <returns>The listener, which the caller disposes at the end of the test.</returns>
    internal static ActivityListener Sampling() => Recording(observe: null);

    /// <summary>Samples every span MailFathom publishes and reports the name of each one as it ends.</summary>
    /// <param name="observe">Called with the operation name of every span that ended, or <see langword="null" /> to record nothing.</param>
    /// <returns>The listener, which the caller disposes at the end of the test.</returns>
    /// <remarks>
    /// The name alone rather than the activity, because the source is the process's and is shared by everything
    /// MailFathom publishes — a test class holding activities another class ended would keep them alive for the run.
    /// </remarks>
    internal static ActivityListener Recording(Action<string>? observe)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => StringComparer.Ordinal.Equals(source.Name, Telemetry.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observe?.Invoke(activity.OperationName),
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }
}
