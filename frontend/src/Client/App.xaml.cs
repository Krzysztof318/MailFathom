// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Deployment;
using MailFathom.Client.Presentation.Settings;
using MailFathom.Client.Presentation.Spaces;
using MailFathom.Client.Presentation.Spaces.Mail;
using MailFathom.Client.Presentation.Threads;
using MailFathom.Client.Presentation.Workspace;
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
    private readonly IOwnerCredentialStore credentialStore;

    /// <summary>Initializes the singleton application object for a head that has said nothing about itself.</summary>
    /// <remarks>
    /// The address comes from what the installation states, and no credential is kept. Both are the safe answers rather
    /// than the useful ones: a head that keeps a password says so by registering the store that keeps it, and one that
    /// has said nothing keeps none. Every head this application actually ships as answers both for itself through the
    /// constructor below.
    /// </remarks>
    public App()
        : this(new ConfiguredDeploymentAddress(), UnkeptOwnerCredentialStore.Instance)
    {
    }

    /// <summary>Initializes the singleton application object for a head that answers both questions itself.</summary>
    /// <param name="deploymentAddress">How this head learns where its deployment is.</param>
    /// <param name="credentialStore">Where this head keeps the credential somebody signs in with, if it keeps one.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The two things a head knows that the application cannot. An address the build stated takes precedence over
    /// whatever the head would have answered, and the wrapping happens here rather than at each head so that adding one
    /// owes an implementation of the interface and nothing else: a head started by an orchestration is served from a
    /// socket of its own while the service listens on another, so neither the origin it was fetched from nor an
    /// installation that does not exist can say where the deployment is. A build that stated nothing changes nothing.
    /// The store is taken as it is given, because where a password may be kept is a property of the operating system a
    /// head runs on and nothing above it may override that.
    /// </remarks>
    internal App(IDeploymentAddressSource deploymentAddress, IOwnerCredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(deploymentAddress);
        ArgumentNullException.ThrowIfNull(credentialStore);

        this.deploymentAddress = new BuildStatedDeploymentAddress(deploymentAddress);
        this.credentialStore = credentialStore;

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
                // Everything this application registers, which is written beside this file rather than in it: a graph
                // composed only inside OnLaunched is one nothing can resolve without a window, and it is what
                // ClientCompositionTests builds and resolves in full.
                .ConfigureServices((context, services) => ClientComposition.Compose(
                    services,
                    context.Configuration,
                    this.deploymentAddress,
                    this.credentialStore))
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
        // this application that cannot be known until two things have been read. An installation nobody has pointed
        // anywhere opens on the screen that asks for a deployment; one that is pointed somewhere and whose head kept a
        // usable credential opens on the application; and one that is pointed somewhere with nothing to present opens
        // on the screen that asks who they are. Nothing in between — a shell that starts and then fails at its first
        // request is exactly what this replaces.
        this.Host = await builder.NavigateAsync<Shell>(
            async (services, navigator) =>
            {
                var pointed = await services.GetRequiredService<DeploymentChoice>().RestoreAsync();

                // Asked whether or not the client is pointed anywhere, because reconciling what was kept against where
                // the client came up pointed is what clears a credential for a deployment nobody is reaching — and a
                // client pointed nowhere is pointed at no deployment at all.
                var signedIn = await services.GetRequiredService<DeploymentSignIn>().RestoreAsync();

                await navigator.NavigateRouteAsync(
                    this,
                    pointed
                        ? signedIn ? ClientRoutes.Workspace : ClientRoutes.SignIn
                        : ClientRoutes.Connect);
            });
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
            new ViewMap<MailThreadPage, MailModel>(),
            new DataViewMap<MailMessagePage, MailModel, ThreadMessageRow>(),
            new ViewMap<CasesPage>(),
            new ViewMap<SettingsPage, SettingsModel>(),
            new ViewMap<ConnectPage, ConnectModel>(),
            new ViewMap<SignInPage, SignInModel>());

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
                            new RouteMap(ClientRoutes.Mail, View: views.FindByView<MailPage>()),
                            new RouteMap(ClientRoutes.Cases, View: views.FindByView<CasesPage>()),
                        ]),
                    new RouteMap(ClientRoutes.MailThread, View: views.FindByView<MailThreadPage>()),
                    new RouteMap(ClientRoutes.MailMessage, View: views.FindByView<MailMessagePage>()),
                    new RouteMap(ClientRoutes.Settings, View: views.FindByViewModel<SettingsModel>()),
                    new RouteMap(ClientRoutes.Connect, View: views.FindByViewModel<ConnectModel>()),
                    new RouteMap(ClientRoutes.SignIn, View: views.FindByViewModel<SignInModel>()),
                ]));
    }
}
