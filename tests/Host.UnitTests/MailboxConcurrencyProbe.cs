// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using NSubstitute;

namespace MailMcp.Host.UnitTests;

/// <summary>Observes how many mailbox work units a synchronization bound keeps in flight at once.</summary>
/// <remarks>
/// Each work unit is held at the point where it would reach the mail server until as many have arrived as the bound is
/// expected to admit. The probe therefore measures what the bound allows rather than what the scheduler happened to
/// interleave: a bound of one releases each entry on its own and can never report two, while a bound of three only
/// releases once three are inside it. Every entry then fails, because these tests are about the bound and not about
/// what a successful run stores.
/// </remarks>
internal sealed class MailboxConcurrencyProbe
{
    private readonly TaskCompletionSource entriesHeldTogether = new();
    private readonly TaskCompletionSource allEntered = new();
    private readonly int expectedEntryCount;
    private readonly int entriesToHoldTogether;
    private int enteredCount;
    private int currentConcurrency;
    private int maxObservedConcurrency;

    /// <summary>Initializes a probe over the work units one bound admits.</summary>
    /// <param name="expectedEntryCount">How many work units the test expects in total, after which <see cref="AllEntered" /> completes.</param>
    /// <param name="entriesToHoldTogether">How many work units are held inside the probe at once, which is the bound under test.</param>
    internal MailboxConcurrencyProbe(int expectedEntryCount, int entriesToHoldTogether)
    {
        this.expectedEntryCount = expectedEntryCount;
        this.entriesToHoldTogether = entriesToHoldTogether;
    }

    /// <summary>Completes once every expected work unit has passed through the probe.</summary>
    internal Task AllEntered => this.allEntered.Task;

    /// <summary>Gets the highest number of work units the probe ever saw inside it at the same time.</summary>
    internal int MaxObservedConcurrency => Volatile.Read(ref this.maxObservedConcurrency);

    /// <summary>Builds the mail server stand-in that every work unit enters this probe through.</summary>
    internal IMailboxSessionFactory CreateSessionFactory()
    {
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => this.EnterAsync());

        return sessionFactory;
    }

    private async Task<IMailboxSession> EnterAsync()
    {
        var concurrency = Interlocked.Increment(ref this.currentConcurrency);

        RaiseTo(ref this.maxObservedConcurrency, concurrency);

        if (concurrency >= this.entriesToHoldTogether)
        {
            this.entriesHeldTogether.TrySetResult();
        }

        await this.entriesHeldTogether.Task;

        Interlocked.Decrement(ref this.currentConcurrency);

        if (Interlocked.Increment(ref this.enteredCount) == this.expectedEntryCount)
        {
            this.allEntered.TrySetResult();
        }

        throw new InvalidOperationException("connect failed");
    }

    /// <summary>Raises a shared maximum to the observed value without losing a concurrent raise.</summary>
    private static void RaiseTo(ref int target, int candidate)
    {
        var observed = Volatile.Read(ref target);

        while (candidate > observed)
        {
            var previous = Interlocked.CompareExchange(ref target, candidate, observed);

            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }
}
