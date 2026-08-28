// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Deployment;
using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.Client;

/// <summary>Everything the client registers, in one place a test can call without starting a head.</summary>
/// <remarks>
/// <para>
/// It is the whole of what <see cref="App" /> hands the host builder's <c>ConfigureServices</c>, moved out of the
/// launch path rather than duplicated beside it: <see cref="App.OnLaunched" /> needs a window, an application object,
/// and a XAML runtime, none of which a unit-test host has, so a graph composed only inside it is a graph first
/// resolved on somebody's machine. Everything else a head is built from — configuration, localization, logging, theme
/// switching, and navigation — stays where it is, because each of those is a call on the builder rather than a
/// registration this application writes.
/// </para>
/// <para>
/// What this does not register is what the builder contributes around it: <c>IStringLocalizer</c>,
/// <c>ILocalizationService</c>, <c>IThemeService</c>, and <c>INavigator</c> come from the <c>Use*</c> calls in
/// <see cref="App.OnLaunched" />, and a screen reaching one of them is reaching the framework rather than this
/// composition.
/// </para>
/// </remarks>
internal static class ClientComposition
{
    /// <summary>Registers everything the client resolves, for a head that has already said how it finds its deployment.</summary>
    /// <param name="services">The collection the head's host is being built from.</param>
    /// <param name="configuration">What the installation states, which is where the deployment settings are read from.</param>
    /// <param name="deploymentAddress">How this head learns where its deployment is, already wrapped in whatever the build stated.</param>
    /// <returns>The same collection, so registration composes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static IServiceCollection Compose(
        IServiceCollection services,
        IConfiguration configuration,
        IDeploymentAddressSource deploymentAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(deploymentAddress);

        ComposeDeployment(services, configuration, deploymentAddress);

        // What the three spaces share, so that moving between them keeps the question somebody was composing and what
        // it would be asked against. One for the run rather than one per model: a model is discarded as its view is
        // navigated away from, and so would be anything it held.
        services.AddSingleton<IWorkspace, SharedWorkspace>();

        // The tree those spaces are scoped by, on the same terms and for the same reason: a tree held per model would
        // forget what was open the moment somebody moved between spaces, and would ask the deployment for the same
        // folders again while doing it. Where it was left outlives the run, which is what makes starting the client
        // again opening it rather than finding one's way back.
        services.AddSingleton<IMailboxTreeMemory, LocalSettingsMailboxTreeMemory>();
        services.AddSingleton<IMailboxTree, DeploymentMailboxTree>();

        // The mail that tree is narrowed to, on the same terms and for the same reason: a list held per model would
        // read the folder's first page again every time somebody moved between spaces, and would forget how far they
        // had scrolled while doing it. Where it was left outlives the run for the place the tree reopens on, and
        // outlives only the run for every other place it visited.
        services.AddSingleton<IMessageListMemory, LocalSettingsMessageListMemory>();
        services.AddSingleton<IMessageList, DeploymentMessageList>();

        // What the deployment allows this caller, for the same reason and on the same terms. It is the one place that
        // answers whether something may be offered, so every screen reads one answer instead of deriving its own from
        // a request the deployment refused — and it keeps itself current by listening where the two things that
        // invalidate it happen.
        services.AddSingleton<IClientSession, DeploymentClientSession>();

        // How many times a client that has lost its deployment asks again before it stops and offers the ask as a
        // button, and what the wait between attempts is measured against. Both are registered rather than written into
        // the session, because what they decide is a policy this composition states and a test states differently.
        services.AddSingleton(DeploymentConnectionRetry.Standard);
        services.AddSingleton(TimeProvider.System);

        return services;
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
    private static void ComposeDeployment(
        IServiceCollection services,
        IConfiguration configuration,
        IDeploymentAddressSource deploymentAddress)
    {
        var stated = configuration.GetSection(DeploymentSettings.SectionName);

        var settings = new DeploymentSettings
        {
            Address = stated[nameof(DeploymentSettings.Address)] ?? string.Empty,
            ClientId = stated[nameof(DeploymentSettings.ClientId)] ?? string.Empty,
        };

        services
            .AddSingleton(settings)
            .AddSingleton(deploymentAddress)
            .AddSingleton<IDeploymentChoiceStore, LocalSettingsDeploymentChoiceStore>()
            .AddSingleton<DeploymentChoice>()
            .AddMailFathomDeployment(new DeploymentOptions(settings.ClientId));
    }
}
