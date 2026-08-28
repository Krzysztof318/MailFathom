// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.Backend;

/// <summary>Which deployment this client is pointed at, for as long as it is pointed there.</summary>
/// <remarks>
/// <para>
/// The one value in this assembly that a person decides at run time rather than an installation states, which is why it
/// is held here rather than on <see cref="DeploymentOptions" />. Nothing here has a default and nothing composes one
/// from a literal: a client that guessed would reach somebody else's deployment, and until something points it
/// somewhere <see cref="Current" /> is <see langword="null" /> and no route can be resolved at all.
/// </para>
/// <para>
/// It is read every time a transport is created rather than captured when the host is composed, which is what makes
/// pointing the client elsewhere something a running window can do. That is also why every caller that talks to a
/// deployment asks <see cref="IHttpClientFactory" /> for a transport per exchange instead of holding one: a captured
/// transport would keep the address it was created with, and the client would go on reaching the deployment somebody
/// had just left.
/// </para>
/// <para>
/// Ending the session is part of pointing it elsewhere rather than something each caller remembers. A credential
/// belongs to an owner on one deployment and means nothing on another, so it is dropped here, where the change is —
/// stated as a reference this type holds rather than as a rule a reviewer has to check for at every call site.
/// </para>
/// </remarks>
public sealed class DeploymentAddress
{
    private readonly SignedInOwner owner;
    private readonly Lock guard = new();
    private Uri? current;

    /// <summary>Initializes the address with the session it ends when the deployment changes.</summary>
    /// <param name="owner">Who is signed in during this run, and where that credential is kept.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner" /> is <see langword="null" />.</exception>
    public DeploymentAddress(SignedInOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        this.owner = owner;
    }

    /// <summary>Raised when the client stops reaching one deployment and starts reaching another.</summary>
    /// <remarks>
    /// Everything the client holds about a deployment describes the one it was reached from, so a move invalidates it
    /// whether or not anybody was signed in. The credential is dropped here already; this is the same fact stated for
    /// readers that hold an answer rather than a credential — announced from the one place the change happens, rather
    /// than left to every caller that points the client somewhere to remember. A move made while somebody was signed in
    /// therefore raises both this and <see cref="SignedInOwner.SignedInChanged" />, which is
    /// deliberate: the two say different things, and a reader that acted on either alone would go on holding an answer
    /// in the case the other one covers.
    /// </remarks>
    public event EventHandler? Moved;

    /// <summary>Gets the deployment this client is pointed at, or <see langword="null" /> where nothing has pointed it yet.</summary>
    /// <remarks>Null is a state a first run is genuinely in rather than a failure: nobody has said where their MailFathom is, and the client's answer to that is to ask.</remarks>
    public Uri? Current
    {
        get
        {
            lock (this.guard)
            {
                return this.current;
            }
        }
    }

    /// <summary>Gets whether this client knows which deployment it reaches.</summary>
    public bool IsPointed => this.Current is not null;

    /// <summary>Points the client at a deployment, ending any session held against a different one.</summary>
    /// <param name="address">The deployment's base address, which every route is resolved against.</param>
    /// <param name="cancellationToken">Abandons clearing the credential the client is leaving behind.</param>
    /// <returns>A task completing once the client is pointed there and nothing of the previous session is held.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="address" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the address is not one this client may be pointed at, as <see cref="DeploymentAddressRule" /> decides.</exception>
    /// <remarks>
    /// Pointing it at the address it already carries changes nothing, session included, so re-reading what was
    /// persisted at every start does not sign anybody out.
    /// <para>
    /// Asynchronous because ending the session reaches the operating system's own secret store, which is where a head
    /// that keeps a credential keeps it. Clearing it as the client is pointed away rather than at the next start is
    /// what keeps a password for a deployment nobody is reaching from lying in a keyring for the rest of the run.
    /// </para>
    /// </remarks>
    public async ValueTask PointAtAsync(Uri address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var refusal = DeploymentAddressRule.Judge(address);

        if (refusal != DeploymentAddressRefusal.None)
        {
            throw new ArgumentException(
                $"{DeploymentAddressRule.Describe(address)} is not an address this client may be pointed at "
                + $"({refusal}).",
                nameof(address));
        }

        bool moved;

        lock (this.guard)
        {
            moved = this.current is not null && this.current != address;
            this.current = address;
        }

        if (moved)
        {
            await this.owner.ForgetAsync(cancellationToken).ConfigureAwait(false);

            this.Moved?.Invoke(this, EventArgs.Empty);
        }
    }
}
