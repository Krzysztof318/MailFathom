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
/// </remarks>
internal sealed class DeploymentMailOwner : IDeploymentMailOwnerSource
{
    /// <summary>The owner the startup gate established, or nothing while it has not run.</summary>
    /// <remarks>
    /// Absence is what is being stored rather than a default owner, which is why the field is nullable and the property
    /// below is not: a deployment before its gate has run holds no owner at all, and a value type's default would read
    /// as one.
    /// </remarks>
    private MailOwnerId? resolvedOwner;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the startup gate has not yet established the owner.</exception>
    public MailOwnerId Owner =>
        this.resolvedOwner
        ?? throw new InvalidOperationException(
            "The owner this deployment serves is read before the startup gate that establishes it has run. Nothing "
            + "served by this host reaches that state; a caller that does is composed outside the host's own ordering.");

    /// <summary>States the owner the startup gate found, which is the one owner this deployment holds.</summary>
    /// <param name="owner">The owner every configured mail account belongs to.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    internal void Resolved(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("The owner a deployment serves is a named one.", nameof(owner));
        }

        this.resolvedOwner = owner;
    }
}
