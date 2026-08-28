// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.Presentation;

/// <summary>
/// The model behind <see cref="Shell"/>, which is the one thing in this application that outlives every screen inside
/// it. What it holds is the rule no single screen could: a session that has ended puts the person in front of the
/// sign-in, wherever in the application they were when it ended.
/// </summary>
/// <remarks>
/// <para>
/// Three different things end a session and none of them belongs to a screen. Somebody signs out from the settings; a
/// deployment stops accepting a credential it once accepted, which the session fetch discovers on whichever screen
/// happens to be open; and the client is pointed at another deployment, which drops the credential that belonged to
/// the one being left. All three are announced in one place —
/// <see cref="SignedInOwner.SignedInChanged" /> — so all three are answered here rather than by each screen
/// remembering to check.
/// </para>
/// <para>
/// What it does about it is navigate, which is the honest answer: the alternative is a shell whose every request fails
/// one at a time while the interface goes on offering things the deployment will refuse. It navigates only when nobody
/// is signed in, so the event a completed sign-in raises moves nothing.
/// </para>
/// <para>
/// It subscribes for the application's lifetime and never unsubscribes, because the shell is the application: this
/// model is built once, with the window, and is gone only when the process is. Nothing here is disposed by navigation,
/// which builds and discards the models inside the shell rather than the shell's own.
/// </para>
/// </remarks>
public partial record ShellModel
{
    private readonly SignedInOwner owner;
    private readonly DeploymentAddress address;
    private readonly INavigator navigator;

    /// <summary>Initializes the shell over the session it watches and the deployment that session belongs to.</summary>
    /// <param name="owner">Who is signed in, which announces when nobody is any more.</param>
    /// <param name="address">Which deployment the client reaches, which decides whether there is anything to sign in to.</param>
    /// <param name="navigator">Where the person is put when the session ends.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public ShellModel(SignedInOwner owner, DeploymentAddress address, INavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(navigator);

        this.owner = owner;
        this.address = address;
        this.navigator = navigator;

        this.owner.SignedInChanged += this.AskWhoTheyAre;
    }

    /// <summary>Puts the person in front of the sign-in once nothing is signed in any more.</summary>
    /// <remarks>
    /// A client that is pointed nowhere is left alone: it has no deployment to sign in to, and whoever pointed it away
    /// is already on the screen that asks for one. Every other case ends on the sign-in with the back stack cleared,
    /// because what is behind it is a session that no longer exists.
    /// <para>
    /// The navigation is started rather than awaited, because the event this answers is synchronous and nothing is
    /// waiting for the screen to change. An <c>async void</c> handler would be the other way of writing it and a worse
    /// one: it would turn a navigation that failed into an unhandled exception on whichever thread happened to end the
    /// session.
    /// </para>
    /// </remarks>
    private void AskWhoTheyAre(object? sender, EventArgs e)
    {
        if (this.owner.IsSignedIn || !this.address.IsPointed)
        {
            return;
        }

        _ = this.navigator.NavigateAsync(new NavigationRequest(
            this,
            new Route(Qualifier: Qualifiers.ClearBackStack, Base: ClientRoutes.SignIn)));
    }
}
