// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>The budget each stage of reaching a submission server is given before it is abandoned.</summary>
/// <param name="Connection">How long the transport to the endpoint may take to open.</param>
/// <param name="Greeting">How long encryption, the greeting, and the capability exchange may take together.</param>
/// <param name="Authentication">How long the server may take to answer one presentation of the account's credential.</param>
/// <param name="Command">How long any command over the established session may take, which the client enforces itself.</param>
/// <param name="Transmission">How long offering the envelope and transmitting the whole message may take together.</param>
/// <remarks>
/// <para>
/// The first three bound the stages of establishing a session, which run inside the attempt budget of the
/// <c>EmailDelivery</c> resilience class. What they add is attribution: an establishment that stops is reported against
/// the stage it stopped in rather than as one unexplained wait, and a stage that expires is a
/// <see cref="TimeoutException" /> rather than a cancellation, so a hung server can never be read as a host shutting
/// down. Their defaults total less than that class's default attempt timeout, which is what keeps a stage able to
/// expire on its own before the enclosing budget takes the attempt away from it.
/// </para>
/// <para>
/// <see cref="Authentication" /> bounds one round trip rather than the whole authentication, because an account
/// whose access token the server refuses presents a renewed one over a second round trip. The exchange that renews it
/// is not inside this budget at all: it is a request to the authorization server, bounded by that dependency class,
/// and holding it here would report its silence against the submission server.
/// </para>
/// <para>
/// <see cref="Command" /> is not one of them. It is applied to the client as its own timeout and bounds a command over
/// the established session, which is outside the establishment attempt, so it is not part of the total above and adding
/// it to one would describe a budget nothing enforces.
/// </para>
/// <para>
/// <see cref="Transmission" /> bounds the submission as a whole, and is generous next to the others on purpose: it has
/// to cover a message of the largest size this deployment composes crossing the network, where the client's own
/// timeout bounds each read and write rather than the transfer. What it is for is a server that stops answering
/// mid-submission, which would otherwise hold an attempt — and the lease under it — for as long as the socket stayed
/// open.
/// </para>
/// </remarks>
internal sealed record MailDeliveryTimeouts(
    TimeSpan Connection,
    TimeSpan Greeting,
    TimeSpan Authentication,
    TimeSpan Command,
    TimeSpan Transmission)
{
    /// <summary>Gets the budgets every delivery session is opened under.</summary>
    internal static MailDeliveryTimeouts Default { get; } = new(
        Connection: TimeSpan.FromSeconds(15),
        Greeting: TimeSpan.FromSeconds(15),
        Authentication: TimeSpan.FromSeconds(20),
        Command: TimeSpan.FromSeconds(30),
        Transmission: TimeSpan.FromMinutes(5));

    /// <summary>Gets the budget one stage is given.</summary>
    /// <param name="phase">The stage about to run.</param>
    /// <returns>How long that stage may take.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="phase" /> is not a defined member.</exception>
    internal TimeSpan For(MailDeliveryPhase phase) => phase switch
    {
        MailDeliveryPhase.Connection => this.Connection,
        MailDeliveryPhase.Greeting => this.Greeting,
        MailDeliveryPhase.Authentication => this.Authentication,
        MailDeliveryPhase.Command => this.Command,
        MailDeliveryPhase.Transmission => this.Transmission,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "No delivery budget is defined for this phase."),
    };
}
