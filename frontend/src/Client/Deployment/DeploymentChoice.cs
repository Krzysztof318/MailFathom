// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;

namespace MailFathom.Client.Deployment;

/// <summary>What became of an address somebody offered the client.</summary>
/// <remarks>
/// One closed set rather than two, because a person typing an address does not care which layer refused it: whether the
/// text was not an address, whether it was one this client may not carry a credential to, or whether nothing answered
/// there, what they get is one sentence and another try. The screen maps each of these to exactly one string, which is
/// why a case added here is a string owed in every language rather than a message composed at the point of failure.
/// </remarks>
internal enum DeploymentChoiceOutcome
{
    /// <summary>The client is now pointed at that deployment, and will be again when it next starts.</summary>
    Accepted = 0,

    /// <summary>The text does not name an address at all.</summary>
    NotAnAddress = 1,

    /// <summary>It is clear text to somewhere that is not this machine, which would hand the sign-in to whatever is on the path.</summary>
    ClearTextOffThisMachine = 2,

    /// <summary>It carries more than an origin — a path, a query, a fragment, or a credential written into it.</summary>
    MoreThanAnOrigin = 3,

    /// <summary>Nothing answered there.</summary>
    Unreachable = 4,

    /// <summary>Something is there and did not answer in time.</summary>
    TimedOut = 5,

    /// <summary>Something answered, and it is not a MailFathom deployment.</summary>
    NotADeployment = 6,
}

/// <summary>Which deployment this installation reaches, and how somebody changes it.</summary>
/// <remarks>
/// <para>
/// The whole of the answer, assembled from the three things that can hold one. A person's own choice outlives every
/// restart and is read first, because it is the most recent thing anybody actually decided. What a build stated comes
/// next, which is how a local orchestration hands its head an address nobody typed. What the head knows for itself is
/// last: a file beside an installed application, or the origin a browser head was served from. When none of them
/// answers, this installation is one nobody has pointed anywhere yet — which is a state rather than a failure, and the
/// client's answer to it is to ask.
/// </para>
/// <para>
/// Changing it is the same act as choosing it the first time, deliberately: one path, judged and proved the same way,
/// so a second deployment is reached by exactly what reached the first. Ending the session that belonged to the
/// previous one is <see cref="DeploymentAddress" />'s and happens as part of pointing the client elsewhere.
/// </para>
/// </remarks>
internal sealed class DeploymentChoice
{
    private readonly IDeploymentChoiceStore store;
    private readonly IDeploymentAddressSource head;
    private readonly DeploymentSettings settings;
    private readonly DeploymentAddress address;
    private readonly DeploymentProbe probe;

    /// <summary>Initializes the choice over what keeps it, what the head knows, and what proves an address.</summary>
    /// <param name="store">Where a person's choice is kept between runs.</param>
    /// <param name="head">What this head knows before anybody has chosen.</param>
    /// <param name="settings">What the installation stated, for the heads that read it.</param>
    /// <param name="address">The address every transport follows.</param>
    /// <param name="probe">How a candidate is proved before it is kept.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public DeploymentChoice(
        IDeploymentChoiceStore store,
        IDeploymentAddressSource head,
        DeploymentSettings settings,
        DeploymentAddress address,
        DeploymentProbe probe)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(probe);

        this.store = store;
        this.head = head;
        this.settings = settings;
        this.address = address;
        this.probe = probe;
    }

    /// <summary>Points the client at whatever was decided before this run.</summary>
    /// <param name="cancellationToken">Abandons clearing a credential the previous run left behind.</param>
    /// <returns><see langword="true" /> when the client is now pointed somewhere; <see langword="false" /> when nobody has said where.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a build or an installation stated an address this client may not be pointed at.</exception>
    /// <remarks>
    /// The two kinds of stated address are held to different standards on purpose. Something a build or an installation
    /// wrote is a statement somebody made and can correct, so one this client may not carry a credential to fails
    /// loudly here rather than being dropped — a configured deployment that was quietly ignored is the worst of the
    /// three outcomes. A kept choice is not a statement anybody can go and read, so one that no longer passes the rule
    /// is simply forgotten and the person is asked again.
    /// </remarks>
    public async ValueTask<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var chosen = this.store.Read();

        if (chosen is not null && DeploymentAddressRule.Judge(chosen) == DeploymentAddressRefusal.None)
        {
            await this.address.PointAtAsync(chosen, cancellationToken).ConfigureAwait(false);

            return true;
        }

        var stated = this.head.Resolve(this.settings);

        if (stated is null)
        {
            return false;
        }

        var refusal = DeploymentAddressRule.Judge(stated);

        if (refusal != DeploymentAddressRefusal.None)
        {
            throw new InvalidOperationException(
                $"{DeploymentAddressRule.Describe(stated)} was stated as the MailFathom deployment this head reaches, "
                + $"and is not one this client may be pointed at ({refusal}). Every request carries the signed-in "
                + "credential, so the address has to be an origin — the scheme, host, and port and nothing else — and "
                + "https to anything but this machine.");
        }

        await this.address.PointAtAsync(stated, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>Judges, proves, keeps, and points the client at an address somebody wrote.</summary>
    /// <param name="written">What they wrote, which may be blank or nonsense.</param>
    /// <param name="cancellationToken">Abandons the attempt.</param>
    /// <returns>What became of it.</returns>
    /// <remarks>
    /// Nothing is kept until something has answered at the address, which is what turns a typing mistake into a
    /// sentence on the screen a person is already looking at rather than into an authentication failure after they have
    /// entered a password. The order is deliberate the other way round too: the rule is applied before anything is
    /// sent, so an address this client may not carry a credential to is never contacted at all.
    /// </remarks>
    public async ValueTask<DeploymentChoiceOutcome> ChooseAsync(
        string? written,
        CancellationToken cancellationToken = default)
    {
        if (!DeploymentAddressText.TryRead(written, out var candidate))
        {
            return DeploymentChoiceOutcome.NotAnAddress;
        }

        var refused = Refused(DeploymentAddressRule.Judge(candidate));

        if (refused is not null)
        {
            return refused.Value;
        }

        try
        {
            await this.probe.ReachAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (DeploymentFailure failure)
        {
            return Unreached(failure.Reason);
        }

        this.store.Write(candidate);

        await this.address.PointAtAsync(candidate, cancellationToken).ConfigureAwait(false);

        return DeploymentChoiceOutcome.Accepted;
    }

    /// <summary>Says what the rule's refusal is, in the terms the screen speaks.</summary>
    private static DeploymentChoiceOutcome? Refused(DeploymentAddressRefusal refusal) => refusal switch
    {
        DeploymentAddressRefusal.None => null,
        DeploymentAddressRefusal.ClearTextOffThisMachine => DeploymentChoiceOutcome.ClearTextOffThisMachine,
        DeploymentAddressRefusal.MoreThanAnOrigin => DeploymentChoiceOutcome.MoreThanAnOrigin,
        _ => DeploymentChoiceOutcome.NotAnAddress,
    };

    /// <summary>Says what an exchange that produced no answer is, in the same terms.</summary>
    /// <remarks>
    /// A refused credential cannot arrive here — the probe presents none, and reads a refusal as a deployment guarding
    /// its client surface — so it falls in with the answer nothing can be made of, which is what it would be.
    /// </remarks>
    private static DeploymentChoiceOutcome Unreached(DeploymentFailureReason reason) => reason switch
    {
        DeploymentFailureReason.Unreachable => DeploymentChoiceOutcome.Unreachable,
        DeploymentFailureReason.TimedOut => DeploymentChoiceOutcome.TimedOut,
        _ => DeploymentChoiceOutcome.NotADeployment,
    };
}
