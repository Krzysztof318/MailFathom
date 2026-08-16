// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>The budget each stage of reaching a submission server is given before it is abandoned.</summary>
/// <param name="Connection">How long the transport to the endpoint may take to open.</param>
/// <param name="Greeting">How long encryption, the greeting, and the capability exchange may take together.</param>
/// <param name="Authentication">How long the server may take to answer the account's credential.</param>
/// <param name="Command">How long any command over the established session may take, which the client enforces itself.</param>
/// <remarks>
/// <para>
/// These sit inside the attempt budget of the <c>EmailDelivery</c> resilience class, which is the value a deployment
/// configures and the one that bounds the whole exchange. What they add is attribution: a submission that stops is
/// reported against the stage it stopped in rather than as one unexplained wait, and a stage that expires is a
/// <see cref="TimeoutException" /> rather than a cancellation, so a hung server can never be read as a host shutting
/// down.
/// </para>
/// <para>
/// The defaults total less than that class's default attempt timeout, which keeps every stage able to expire on its own
/// before the enclosing budget takes the attempt away from it — the arrangement that makes the attribution worth
/// anything.
/// </para>
/// </remarks>
internal sealed record MailDeliveryTimeouts(
    TimeSpan Connection,
    TimeSpan Greeting,
    TimeSpan Authentication,
    TimeSpan Command)
{
    /// <summary>Gets the budgets every delivery session is opened under.</summary>
    internal static MailDeliveryTimeouts Default { get; } = new(
        Connection: TimeSpan.FromSeconds(15),
        Greeting: TimeSpan.FromSeconds(15),
        Authentication: TimeSpan.FromSeconds(20),
        Command: TimeSpan.FromSeconds(30));

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
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "No delivery budget is defined for this phase."),
    };
}
