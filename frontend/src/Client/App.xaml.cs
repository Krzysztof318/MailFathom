// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Client.Backend;
using MailFathom.Client.Deployment;

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
                .ConfigureServices((context, services) => services.AddMailFathomDeployment(
                    this.ComposeDeployment(context.Configuration)))
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

        this.Host = await builder.NavigateAsync<Shell>();
    }

    /// <summary>Reads which deployment this head reaches, and as which registered client.</summary>
    /// <remarks>
    /// <para>
    /// The composition root's whole part in it. Where the deployment is, is the head's answer and what to present is
    /// the installation's, and neither is decided in <c>Client.Backend</c>: that assembly has no default address and
    /// composes none from a literal, so a client that reached the wrong deployment would have been sent there by
    /// something readable rather than by a constant nobody saw. A head that cannot answer says so here, while the host
    /// is being built, rather than opening a window that fails at its first request.
    /// </para>
    /// <para>
    /// The two values are read by name rather than bound onto the record. Binding is reflection over properties, and
    /// the browser head is trimmed — the same reason this stack source-generates every serializer it uses — so a bound
    /// section is one the trimmer can quietly empty. Two keys are not worth a source-generated binder either, and
    /// reading them is the shape that cannot be trimmed away.
    /// </para>
    /// </remarks>
    private DeploymentOptions ComposeDeployment(IConfiguration configuration)
    {
        var stated = configuration.GetSection(DeploymentSettings.SectionName);

        var settings = new DeploymentSettings
        {
            Address = stated[nameof(DeploymentSettings.Address)] ?? string.Empty,
            ClientId = stated[nameof(DeploymentSettings.ClientId)] ?? string.Empty,
        };

        return new DeploymentOptions(this.deploymentAddress.Resolve(settings), settings.ClientId);
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
