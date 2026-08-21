// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Client;

/// <summary>
/// The client's composition root: it builds the host every head starts through, configures logging and services, and
/// hands navigation the routes the application is reachable by. It holds no behavior of its own, the way
/// <c>backend/src/Host</c> holds none.
/// </summary>
public partial class App : Application
{
    /// <summary>Initializes the singleton application object.</summary>
    public App()
    {
        this.InitializeComponent();
    }

    /// <summary>The window the application was launched into.</summary>
    protected Window? MainWindow { get; private set; }

    /// <summary>The host the application's services are resolved from.</summary>
    protected IHost? Host { get; private set; }

    /// <inheritdoc />
    [SuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Navigation resolves a view from its model through the registry built above, so every type it reaches is rooted by this method.")]
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var builder = this.CreateBuilder(args)
            // Navigation for the toolkit controls a shell is built from, such as TabBar and NavigationView.
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment()
                                ? LogLevel.Information
                                : LogLevel.Warning)
                        .CoreLogLevel(LogLevel.Warning),
                    enableUnoLogging: true)
                .UseNavigation(ReactiveViewModelMappings.ViewModelMappings, RegisterRoutes));

        this.MainWindow = builder.Window;

#if DEBUG
        this.MainWindow.UseStudio();
#endif
        this.MainWindow.SetWindowIcon();

        this.Host = await builder.NavigateAsync<Shell>();
    }

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
    {
        views.Register(
            new ViewMap(ViewModel: typeof(ShellModel)),
            new ViewMap<MainPage, MainModel>());

        routes.Register(
            new RouteMap(
                "",
                View: views.FindByViewModel<ShellModel>(),
                Nested:
                [
                    new RouteMap("Main", View: views.FindByViewModel<MainModel>(), IsDefault: true),
                ]));
    }
}
