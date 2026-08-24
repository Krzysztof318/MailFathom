// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Holds the owner this deployment serves, once the gate that reads it has established there is exactly one.</summary>
/// <remarks>
/// <para>
/// A singleton because the answer is a property of the deployment rather than of a request, and because every admitted
/// caller on a mail-reading surface is composed against it: resolving it per request would put a database read in front
/// of every one of them to establish a value that cannot change while the process runs.
/// </para>
/// <para>
/// Reading it before the gate has settled it fails rather than answering, because the alternative is a default owner
/// nobody named and callers composed against one would read whichever mail a query matched. The window that reading
/// belongs to is a real one rather than a wiring defect alone: <see cref="DeploymentMailOwnerStartupGate" /> is an
/// ordinary <see cref="IHostedService" /> and the web host's own is registered while the builder runs, so the listener
/// is already accepting connections while the gate runs. What holds traffic off that window is the startup probe,
/// which reports the deployment unstarted until every gate in <see cref="HostStartupGates" /> has completed.
/// </para>
/// <para>
/// The owner is written once from the startup path and read from every request thread afterwards, and both take the
/// same lock, as <see cref="HostStartupGates" /> does for the same shape. The write is one assignment, but the value
/// is a multi-field struct, so nothing about a bare field would establish that a thread which observes the write
/// observes the whole of it, or observes it at all. That is what the lock is for rather than contention: it is
/// uncontended for the life of the process, since the one write happens before any request the read serves.
/// </para>
/// </remarks>
internal sealed class DeploymentMailOwner : IDeploymentMailOwnerSource
{
    private readonly Lock mutex = new();

    /// <summary>The owner the startup gate established, or nothing while it has not run.</summary>
    /// <remarks>
    /// Absence is what is being stored rather than a default owner, which is why the field is nullable and the property
    /// below is not: a deployment before its gate has run holds no owner at all, and a value type's default would read
    /// as one. Every read and write of it is taken under <see cref="mutex" />.
    /// </remarks>
    private MailOwnerId? resolvedOwner;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the startup gate has not yet established the owner.</exception>
    public MailOwnerId Owner
    {
        get
        {
            lock (this.mutex)
            {
                return this.resolvedOwner
                    ?? throw new InvalidOperationException(
                        "The owner this deployment serves is read before the startup gate that establishes it has "
                        + "run. Either the process is still starting, which the startup probe reports until every "
                        + "gate has completed, or the caller is composed outside the host's own startup ordering.");
            }
        }
    }

    /// <summary>States the owner the startup gate found, which is the one owner this deployment holds.</summary>
    /// <param name="owner">The owner every configured mail account belongs to.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    internal void Resolved(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("The owner a deployment serves is a named one.", nameof(owner));
        }

        lock (this.mutex)
        {
            this.resolvedOwner = owner;
        }
    }
}
