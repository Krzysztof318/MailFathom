// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.Client.UnitTests;

/// <summary>The route tree every head starts through.</summary>
public sealed class ClientNavigationTests
{
    /// <summary>
    /// The root has a safe default so Uno cannot accept an empty launch route before asking the conditional startup
    /// callback where restored application state belongs.
    /// </summary>
    [Fact]
    public void RegisterRoutes_TheRootRoute_HasConnectAsItsDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        var views = new ViewRegistry(services);
        var routes = new RouteRegistry(services);

        // Act
        App.RegisterRoutes(views, routes);

        // Assert
        var root = Assert.Single(routes.Items);
        var defaultRoute = Assert.Single(root.Nested, route => route.IsDefault);

        Assert.Equal(ClientRoutes.Connect, defaultRoute.Path);
    }
}
