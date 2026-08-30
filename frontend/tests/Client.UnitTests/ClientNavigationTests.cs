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

    /// <summary>Every registered view is reachable, so a screen added to the map cannot sit nowhere in the tree.</summary>
    [Fact]
    public void RegisterRoutes_EveryViewMap_HasARouteMap()
    {
        // Arrange
        var services = new ServiceCollection();
        var views = new ViewRegistry(services);
        var routes = new RouteRegistry(services);

        // Act
        App.RegisterRoutes(views, routes);

        var routed = Flatten(routes.Items).Select(route => route.View).ToHashSet();

        // Assert
        Assert.NotEmpty(views.Items);
        Assert.All(views.Items, map => Assert.Contains(map, routed));
    }

    /// <summary>The workspace still opens on Discover, which is the default the launch callback has not yet replaced.</summary>
    [Fact]
    public void RegisterRoutes_TheWorkspaceRoute_HasDiscoverAsItsDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        var views = new ViewRegistry(services);
        var routes = new RouteRegistry(services);

        // Act
        App.RegisterRoutes(views, routes);

        // Assert
        var root = Assert.Single(routes.Items);
        var workspace = Assert.Single(root.Nested, route => route.Path == ClientRoutes.Workspace);
        var defaultSpace = Assert.Single(workspace.Nested, route => route.IsDefault);

        Assert.Equal(ClientRoutes.Discover, defaultSpace.Path);
    }

    private static IEnumerable<RouteMap> Flatten(IEnumerable<RouteMap> routes)
    {
        foreach (var route in routes)
        {
            yield return route;

            if (route.Nested is { } nested)
            {
                foreach (var child in Flatten(nested))
                {
                    yield return child;
                }
            }
        }
    }
}
