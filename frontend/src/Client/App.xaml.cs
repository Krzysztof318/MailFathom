// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Client.Backend;
using MailFathom.Client.Deployment;
using MailFathom.Client.Presentation.Settings;
using MailFathom.Client.Presentation.Spaces;
using MailFathom.Client.Presentation.Spaces.Mail;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.Client;

/// <summary>
/// The client's composition root: it builds the host every head starts through, configures logging and services, and
/// hands navigation the routes the application is reachable by. It holds no behavior of its own, the way
/// <c>backend/src/Host</c> holds none.
/// </summary>
public partial class App : Application
{
    private readonly BuildStatedDeploymentAddress deploymentAddress;

    /// <summary>Initializes the singleton application object for a head that is installed rather than served.</summary>
    /// <remarks>The address comes from what the installation states, which is what a desktop head reads, unless the build that produced this head stated one — see the constructor below. A head served by the deployment it talks to passes its own source instead, exactly as it registers its own sign-in redirect listener.</remarks>
    public App()
        : this(new ConfiguredDeploymentAddress())
    {
    }

    /// <summary>Initializes the singleton application object for a head that answers the address question itself.</summary>
    /// <param name="deploymentAddress">How this head learns where its deployment is.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deploymentAddress" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An address the build stated takes precedence over whatever the head would have answered, and the wrapping
    /// happens here rather than at each head so that adding one owes an implementation of the interface and nothing
    /// else. It is not a head's answer: a head started by an orchestration is served from a socket of its own while
    /// the service listens on another, so neither the origin it was fetched from nor an installation that does not
    /// exist can say where the deployment is. A build that stated nothing changes nothing.
    /// </remarks>
    internal App(IDeploymentAddressSource deploymentAddress)
    {
        ArgumentNullException.ThrowIfNull(deploymentAddress);

        this.deploymentAddress = new BuildStatedDeploymentAddress(deploymentAddress);

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
                // Embedded rather than a file beside the application, which is the one source every head can read: a
                // browser head has no file system to reach and no process environment to inherit, so anything it is to
                // know at startup has to travel inside the bundle. A desktop installation that has to state something
                // of its own writes appsettings.json beside the executable, which the same reader layers on top.
                .UseConfiguration(configure: configuration => configuration.EmbeddedSource<App>())
                // Which language the application is read in. The cultures it offers are the ones
                // `LocalizationConfiguration:Cultures` names in the file above, and the choice a person makes is
                // written to a settings file of this application's own so it survives a restart — which is the only
                // point at which it takes effect, because applying a culture is what Uno does on the next launch
                // rather than to a visual tree already built. The screen offering it says so.
                .UseLocalization()
                .ConfigureServices((context, services) =>
                {
                    this.ComposeDeployment(services, context.Configuration);

                    // What the three spaces share, so that moving between them keeps the question somebody was
                    // composing and what it would be asked against. One for the run rather than one per model: a
                    // model is discarded as its view is navigated away from, and so would be anything it held.
                    services.AddSingleton<IWorkspace, SharedWorkspace>();

                    // What the deployment allows this caller, for the same reason and on the same terms. It is the
                    // one place that answers whether something may be offered, so every screen reads one answer
                    // instead of deriving its own from a request the deployment refused — and it keeps itself
                    // current by listening where the two things that invalidate it happen.
                    services.AddSingleton<IClientSession, DeploymentClientSession>();

                    // How many times a client that has lost its deployment asks again before it stops and offers the
                    // ask as a button, and what the wait between attempts is measured against. Both are registered
                    // rather than written into the session, because what they decide is a policy this composition
                    // states and a test states differently.
                    services.AddSingleton(DeploymentConnectionRetry.Standard);
                    services.AddSingleton(TimeProvider.System);
                })
                // Light, dark, and follow-the-system, with the choice written to the platform's own settings store so
                // the application starts the way it was left. Nothing here decides which one: AppTheme.System is the
                // default the service reads back when nothing was ever chosen.
                .UseThemeSwitching()
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

        // Where the client starts is decided here rather than by a route's default, because it is the one thing about
        // this application that cannot be known until something has been read: an installation that has been pointed at
        // a deployment opens on the application, and one that has not opens on the screen that asks. Nothing in between
        // — a shell that starts and then fails at its first request is exactly what this replaces.
        this.Host = await builder.NavigateAsync<Shell>(
            async (services, navigator) =>
            {
                var pointed = services.GetRequiredService<DeploymentChoice>().Restore();

                await navigator.NavigateRouteAsync(
                    this,
                    pointed ? ClientRoutes.Workspace : ClientRoutes.Connect);
            });
    }

    /// <summary>Registers everything that decides which deployment this head reaches, and how it is reached.</summary>
    /// <remarks>
    /// <para>
    /// The composition root's whole part in it. Nothing here names a deployment and <c>Client.Backend</c> has no
    /// default address and composes none from a literal, so a client that reached the wrong one would have been sent
    /// there by something readable — a file somebody wrote, a build somebody ran, or an address somebody typed.
    /// </para>
    /// <para>
    /// What each of the three registrations is for: the settings are what an installation stated, the source is what
    /// this head knows for itself, and the store is where a person's own choice outlives a restart.
    /// <see cref="DeploymentChoice" /> is where they meet, and it is asked once, after the host is built and before
    /// anything is navigated to.
    /// </para>
    /// <para>
    /// The stated values are read by name rather than bound onto the record. Binding is reflection over properties, and
    /// the browser head is trimmed — the same reason this stack source-generates every serializer it uses — so a bound
    /// section is one the trimmer can quietly empty. Two keys are not worth a source-generated binder either, and
    /// reading them is the shape that cannot be trimmed away.
    /// </para>
    /// </remarks>
    private void ComposeDeployment(IServiceCollection services, IConfiguration configuration)
    {
        var stated = configuration.GetSection(DeploymentSettings.SectionName);

        var settings = new DeploymentSettings
        {
            Address = stated[nameof(DeploymentSettings.Address)] ?? string.Empty,
            ClientId = stated[nameof(DeploymentSettings.ClientId)] ?? string.Empty,
        };

        services
            .AddSingleton(settings)
            .AddSingleton<IDeploymentAddressSource>(this.deploymentAddress)
            .AddSingleton<IDeploymentChoiceStore, LocalSettingsDeploymentChoiceStore>()
            .AddSingleton<DeploymentChoice>()
            .AddMailFathomDeployment(new DeploymentOptions(settings.ClientId));
    }

    /// <summary>
    /// Every screen the client is reachable by, as a route rather than as content something swaps by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three spaces are nested inside the frame that holds them, which is what lets the rail and the bottom
    /// navigation name the same destinations and move the same content area. Settings sits beside that frame rather
    /// than inside it: it is not a space, and registering it a level up is what makes going to it a screen somebody
    /// comes back from — a route nested among the three would be switched to like a fourth space, leaving the
    /// workspace with nothing behind it to return to.
    /// </para>
    /// <para>
    /// Registering them as routes is also what makes the Android system back gesture and the browser's history move
    /// through the client's own screens — Uno's navigation is what answers both, and a screen reached by swapping
    /// content by hand is a screen neither of them knows about.
    /// </para>
    /// </remarks>
    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
    {
        views.Register(
            new ViewMap(ViewModel: typeof(ShellModel)),
            new ViewMap<WorkspacePage, WorkspaceModel>(),
            new ViewMap<DiscoverPage>(),
            new ViewMap<MailPage, MailModel>(),
            new ViewMap<CasesPage>(),
            new ViewMap<SettingsPage, SettingsModel>(),
            new ViewMap<ConnectPage, ConnectModel>());

        // No default among the three the shell holds, deliberately. Which of them a launch opens on is what
        // OnLaunched decides from whether this installation has been pointed at a deployment, and a route marked
        // default here would be the second answer to that question. The default inside the frame is a different
        // question — which space the workspace opens on — and Discover carries it.
        routes.Register(
            new RouteMap(
                "",
                View: views.FindByViewModel<ShellModel>(),
                Nested:
                [
                    new RouteMap(
                        ClientRoutes.Workspace,
                        View: views.FindByViewModel<WorkspaceModel>(),
                        Nested:
                        [
                            new RouteMap(ClientRoutes.Discover, View: views.FindByView<DiscoverPage>(), IsDefault: true),
                            new RouteMap(ClientRoutes.Mail, View: views.FindByViewModel<MailModel>()),
                            new RouteMap(ClientRoutes.Cases, View: views.FindByView<CasesPage>()),
                        ]),
                    new RouteMap(ClientRoutes.Settings, View: views.FindByViewModel<SettingsModel>()),
                    new RouteMap(ClientRoutes.Connect, View: views.FindByViewModel<ConnectModel>()),
                ]));
    }
}
