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
/// Reading it before the gate has settled it is a composition defect rather than an operator's problem, so it fails as
/// one. Nothing in the request pipeline can reach that state — the gate runs before the listener opens and refuses to
/// let the host finish starting otherwise — which is what makes the refusal below a statement about wiring rather than
/// about a deployment.
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
                        + "run. Nothing served by this host reaches that state; a caller that does is composed "
                        + "outside the host's own ordering.");
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
