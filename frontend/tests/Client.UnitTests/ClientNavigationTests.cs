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

    /// <summary>
    /// Every model a route names publishes one public constructor, which is what lets navigation build the screen it
    /// belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uno builds a routed screen's data context by reflection: it takes the bindable type the MVUX generator emits
    /// beside the model, calls <see cref="Type.GetConstructors()" />, uses the <em>first</em> one — an order the
    /// runtime does not define — and fills each of its parameters from the container and from nowhere else. The
    /// generator mirrors the model's public constructors onto that type, so a model with two of them leaves the choice
    /// to reflection ordering.
    /// </para>
    /// <para>
    /// What the losing side costs is why this is asserted rather than left to review. A constructor chosen for the
    /// wrong route is handed <see langword="null" /> for the parameters that route carried no data for, the guard
    /// inside it throws, and Uno swallows that and leaves the page with no data context at all. Every binding on the
    /// page then keeps its target's default — a feed bound to nothing renders as loading for good, an overlay whose
    /// visibility never arrives stays over what it covers, and a command that is null is inert — so the screen hangs
    /// with no error in any log, no failed request to find, and no request made at all. That is
    /// <see href="https://github.com/Krzysztof318/MailFathom/issues/1371">issue 1371</see>, and it reached a released
    /// client because nothing here read the constructors.
    /// </para>
    /// </remarks>
    [Fact]
    public void RegisterRoutes_EveryRoutedModel_PublishesOneConstructorForNavigationToBuildItThrough()
    {
        // Arrange
        var services = new ServiceCollection();
        var views = new ViewRegistry(services);
        var routes = new RouteRegistry(services);

        // Act
        App.RegisterRoutes(views, routes);

        string[] ambiguous =
        [
            .. views.Items
                .Select(static map => map.ViewModel)
                .OfType<Type>()
                .Distinct()
                .Where(static model => model.GetConstructors().Length != 1)
                .Select(static model =>
                    $"{model.FullName}: {model.GetConstructors().Length} public constructors"),
        ];

        // Assert
        // The registry is read for emptiness as well, because a registration that named no model would otherwise
        // report a route tree in which every model is unambiguous as one in which none was looked at.
        Assert.NotEmpty(views.Items);

        if (ambiguous.Length > 0)
        {
            Assert.Fail(
                $"Navigation cannot decide which constructor to build these models through:{Environment.NewLine}  "
                + string.Join($"{Environment.NewLine}  ", ambiguous));
        }
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
