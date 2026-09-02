// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Hands out the right to run one job, under a ceiling for the process and a ceiling for the job's own type.</summary>
/// <remarks>
/// <para>
/// The bound belongs to the process rather than to a pass, which is why this is held once and shared: a pass is a unit
/// of claiming, and how much of the instance background work may take is a statement about the instance. A deployment
/// running several replicas therefore bounds in-flight work at this ceiling times the replica count, which is legible
/// but is not a deployment-wide limit; providing one would need a counted claim or an advisory lock, and nothing has
/// asked for it.
/// </para>
/// <para>
/// <b>The type's slot is taken before the process slot, and that ordering is the whole isolation guarantee.</b> A job
/// waiting for its own type to free up holds nothing of the process ceiling, so a flood of one kind of work cannot
/// occupy the capacity another kind would have run in. Taking the process slot first would let a queue of one type sit
/// on every slot while blocked, which is the starvation the per-type ceiling exists to prevent.
/// </para>
/// <para>
/// Every declared type gets its own slots at construction rather than on first use. The set is closed, so there is
/// nothing to grow at run time and no lock to take on the way to a permit.
/// </para>
/// </remarks>
public sealed class JobConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim processSlots;
    private readonly Dictionary<JobType, SemaphoreSlim> slotsByType;

    /// <summary>Initializes the gate from the capacity the deployment configured.</summary>
    /// <param name="settings">The process ceiling and the per-type ceiling this gate hands out under.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    public JobConcurrencyGate(JobCapacitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.processSlots = new SemaphoreSlim(settings.MaxConcurrentJobs, settings.MaxConcurrentJobs);
        this.slotsByType = JobType.All.ToDictionary(
            jobType => jobType,
            _ => new SemaphoreSlim(settings.MaxConcurrentJobsPerType, settings.MaxConcurrentJobsPerType));
    }

    /// <summary>Waits until this process may run one more job of the given type.</summary>
    /// <param name="jobType">The type the job about to run belongs to.</param>
    /// <param name="cancellationToken">Abandons the wait.</param>
    /// <returns>The held capacity, which is given back when it is disposed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="jobType" /> names no declared job type.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled while waiting.</exception>
    /// <remarks>A cancelled wait gives back whatever it had already taken, so an abandoned acquisition leaves the ceilings exactly where it found them.</remarks>
    public async Task<IDisposable> AcquireAsync(JobType jobType, CancellationToken cancellationToken)
    {
        if (!this.slotsByType.TryGetValue(jobType, out var typeSlots))
        {
            throw new ArgumentException($"'{jobType}' names no declared job type.", nameof(jobType));
        }

        await typeSlots.WaitAsync(cancellationToken);

        try
        {
            await this.processSlots.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            typeSlots.Release();

            throw;
        }

        return new HeldCapacity(this.processSlots, typeSlots);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.processSlots.Dispose();

        foreach (var typeSlots in this.slotsByType.Values)
        {
            typeSlots.Dispose();
        }
    }

    /// <summary>The capacity one job holds while it runs, given back once and only once.</summary>
    /// <remarks>
    /// Disposing twice would release a permit this job never held and let one more job past the ceiling, so the release
    /// is guarded rather than trusted to a caller's <c>using</c> being the only one.
    /// </remarks>
    private sealed class HeldCapacity(SemaphoreSlim processSlots, SemaphoreSlim typeSlots) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.released, 1) == 1)
            {
                return;
            }

            processSlots.Release();
            typeSlots.Release();
        }
    }
}
