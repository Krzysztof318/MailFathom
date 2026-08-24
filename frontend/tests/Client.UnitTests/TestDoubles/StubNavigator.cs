// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>Records where a model asked to go, instead of moving a frame nothing here has.</summary>
/// <remarks>Whether a screen navigated at all is the assertion worth making from a unit test: moving a frame needs a running head and belongs to whatever UI suite is added later, but a model that leaves a screen only when an address was accepted is ordinary logic and is asserted here.</remarks>
internal sealed class StubNavigator : INavigator
{
    /// <summary>Gets the requests the model made, in order.</summary>
    internal List<NavigationRequest> Requests { get; } = [];

    /// <inheritdoc />
    public Route? Route => Uno.Extensions.Navigation.Route.Empty;

    /// <inheritdoc />
    public Task<bool> CanNavigate(Route route) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<NavigationResponse?> NavigateAsync(NavigationRequest request)
    {
        this.Requests.Add(request);

        return Task.FromResult<NavigationResponse?>(null);
    }
}
