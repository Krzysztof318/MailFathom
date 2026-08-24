// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Deployment;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation;

/// <summary>
/// The model behind <see cref="ConnectPage"/>: where a person says which MailFathom is theirs, and where they say it
/// again when it is a different one.
/// </summary>
/// <remarks>
/// <para>
/// One screen for both, because they are one act. A first start has nothing to reach and asks; somebody moving between
/// a test deployment and a real one comes back here and is asked the same question, with the address they are on
/// already in the box so that changing it is an edit rather than a retyping.
/// </para>
/// <para>
/// Nothing is stored until something has answered at the address. That is what the third of this screen's reasons for
/// existing is: a person who mistypes is told the client cannot reach it while they are still on this screen, rather
/// than being handed an authentication failure for what was a connection problem.
/// </para>
/// </remarks>
internal sealed partial record ConnectModel
{
    private readonly DeploymentChoice choice;
    private readonly DeploymentAddress address;
    private readonly INavigator navigator;
    private readonly IStringLocalizer localizer;

    /// <summary>Initializes the model over what decides an address and where the client goes once one is decided.</summary>
    /// <param name="choice">Which deployment this installation reaches, and how it is changed.</param>
    /// <param name="address">Where the client is pointed now, which is what the box opens on.</param>
    /// <param name="navigator">Where the person goes once an address has been accepted.</param>
    /// <param name="localizer">Where the sentence explaining a refusal comes from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public ConnectModel(
        DeploymentChoice choice,
        DeploymentAddress address,
        INavigator navigator,
        IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(localizer);

        this.choice = choice;
        this.address = address;
        this.navigator = navigator;
        this.localizer = localizer;
    }

    /// <summary>The address as it is being written, which opens on whatever the client is pointed at.</summary>
    /// <remarks>An installation nobody has pointed anywhere opens on an empty box rather than on a suggestion, for the reason nothing in this client composes a default address: a value offered here is one somebody would accept without reading it.</remarks>
    public IState<string> Address => State.Value(this, () => this.address.Current?.AbsoluteUri ?? string.Empty);

    /// <summary>Why the last attempt was not accepted, or empty where nothing has been refused.</summary>
    /// <remarks>
    /// A sentence rather than the failure itself. Everything <c>Client.Backend</c> raises carries an English message
    /// written for a log, and one of them can carry text from a machine this process does not own, so the screen says
    /// what happened in the language being read in and the outcome is what chooses which sentence.
    /// </remarks>
    public IState<string> Refusal => State.Value(this, () => string.Empty);

    /// <summary>Whether anything was refused, which is what puts the sentence on the screen.</summary>
    /// <remarks>
    /// Derived from the sentence rather than kept beside it: two values saying the same thing are two values that can
    /// disagree, and the one that would be wrong is the one deciding whether a message is shown at all. It reads a
    /// blank as nothing having been refused because MVUX carries one that way — a state set to an empty string reaches
    /// a reader as no value rather than as an empty one — and both readings mean the same thing here.
    /// </remarks>
    public IFeed<bool> IsRefused => this.Refusal.Select(refusal => !string.IsNullOrEmpty(refusal));

    /// <summary>Whether the client is asking the address what is there.</summary>
    /// <remarks>Reaching an address that is not answering takes as long as the configured timeout, which is long enough that a screen saying nothing would read as one that had stopped working.</remarks>
    public IState<bool> IsAsking => State.Value(this, () => false);

    /// <summary>Whether the address may be offered, which it may not be while the last one is still being asked.</summary>
    public IFeed<bool> CanAsk => this.IsAsking.Select(asking => !asking);

    /// <summary>Takes the address that has been written, and moves on where something answered at it.</summary>
    /// <param name="ct">Abandons the attempt.</param>
    /// <returns>A task completing once the address has been accepted or refused.</returns>
    public async ValueTask Connect(CancellationToken ct)
    {
        await this.IsAsking.SetAsync(true, ct).ConfigureAwait(false);
        await this.Refusal.SetAsync(string.Empty, ct).ConfigureAwait(false);

        try
        {
            var outcome = await this.choice
                .ChooseAsync(await this.Address, ct)
                .ConfigureAwait(false);

            if (outcome != DeploymentChoiceOutcome.Accepted)
            {
                await this.Refusal.SetAsync(this.Explain(outcome), ct).ConfigureAwait(false);

                return;
            }
        }
        finally
        {
            await this.IsAsking.SetAsync(false, ct).ConfigureAwait(false);
        }

        // The back stack is cleared rather than added to: the screen behind this one belonged to a deployment the
        // client has just stopped reaching, and the system back gesture returning to it would offer a way back into an
        // application pointed somewhere it no longer is.
        //
        // The request is built and handed to the interface rather than composed by one of the NavigatorExtensions
        // helpers. Those exist to turn a view-model type into a route, which is work this does not need — the route is
        // named — and they do it by resolving IRouteResolver out of a service provider they reach through the
        // navigator, which is the whole navigation graph rather than the seam a model should depend on.
        await this.navigator
            .NavigateAsync(
                new NavigationRequest(
                    this,
                    new Route(Qualifier: Qualifiers.ClearBackStack, Base: ClientRoutes.Workspace),
                    ct))
            .ConfigureAwait(false);
    }

    /// <summary>Says what became of an address in the language the person is reading in.</summary>
    /// <remarks>The resource name is composed from the outcome, so a case added to that set is a missing string the resource-table test names rather than a message that quietly falls back to a key.</remarks>
    private string Explain(DeploymentChoiceOutcome outcome) =>
        this.localizer[$"ConnectPage.Refusal.{outcome}"];
}
