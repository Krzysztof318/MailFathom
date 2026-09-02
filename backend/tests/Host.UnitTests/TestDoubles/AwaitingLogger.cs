// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Logging;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A logger a test can wait on, so an assertion never races the run that produces the line.</summary>
/// <remarks>
/// <see cref="RecordingLogger{TCategory}" /> beside this one answers what was written once a test already knows the
/// work is done. A worker that never ends offers no such moment, so the log line itself is the signal here.
/// </remarks>
internal sealed class AwaitingLogger<TCategory> : ILogger<TCategory>
{
    /// <summary>Guards against a line that never arrives. No assertion depends on how long a run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private readonly Lock recordedMessages = new();
    private readonly List<string> messages = [];
    private readonly List<Expectation> expectations = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (this.recordedMessages)
            {
                return [.. this.messages];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        List<Expectation> satisfied;

        lock (this.recordedMessages)
        {
            this.messages.Add(formatter(state, exception));
            satisfied = [.. this.expectations.Where(this.IsSatisfied)];
            this.expectations.RemoveAll(satisfied.Contains);
        }

        // Completed outside the lock, so a continuation that logs cannot re-enter it.
        foreach (var expectation in satisfied)
        {
            expectation.Signal.TrySetResult();
        }
    }

    /// <summary>Waits until a message containing the fragment has been logged the given number of times.</summary>
    /// <param name="fragment">The text the awaited line contains.</param>
    /// <param name="occurrences">How many such lines to wait for.</param>
    /// <param name="cancellationToken">Ends the wait when the test does.</param>
    /// <returns>A task that completes once the lines have been written.</returns>
    public Task WaitForOccurrences(string fragment, int occurrences, CancellationToken cancellationToken)
    {
        var expectation = new Expectation(
            fragment,
            occurrences,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        lock (this.recordedMessages)
        {
            if (this.IsSatisfied(expectation))
            {
                return Task.CompletedTask;
            }

            this.expectations.Add(expectation);
        }

        return expectation.Signal.Task.WaitAsync(DeadlockGuard, cancellationToken);
    }

    private bool IsSatisfied(Expectation expectation) => this.messages
        .Count(message => message.Contains(expectation.Fragment, StringComparison.Ordinal))
        >= expectation.Occurrences;

    private sealed record Expectation(string Fragment, int Occurrences, TaskCompletionSource Signal);
}
