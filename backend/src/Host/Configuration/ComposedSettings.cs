// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Answering;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Infrastructure.Rules;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration;

/// <summary>States every rule a start applies to a section it reads while composing itself rather than through options.</summary>
/// <remarks>
/// <para>
/// <see cref="BoundSettings" /> is the other half of what a start judges, and the split between them is not a
/// preference: a section whose value decides which sockets are opened, whether an endpoint is mapped, or whether a
/// declared rule can be compiled at all is read before a container exists, so the options framework's validators are
/// not available to it and the refusal is taken during composition. That makes it exactly the kind of rule a
/// configuration write would otherwise escape, which is why it is stated here rather than inline in the composition
/// root: the host refuses the first refusal, and a write is refused by every one of them.
/// </para>
/// <para>
/// Nothing here registers a service or reads anything but the configuration it is handed, so a candidate configuration
/// is judged by it exactly as the deployment's own is. Each rule reads the section itself, because the value it needs
/// is the candidate's rather than the running process's, and a section read twice costs one binding of a handful of
/// keys against a start that opens sockets.
/// </para>
/// </remarks>
internal static class ComposedSettings
{
    /// <summary>Finds every refusal these settings carry, in the order a start would meet them.</summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <param name="timeProvider">Supplies the date the declared synchronization bounds are read against.</param>
    /// <returns>One refusal per section that would stop a start, empty when none would.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a section will not bind at all and no earlier group had already answered with a refusal, which is the only case in which nothing better than the binder's own sentence is held.</exception>
    public static IReadOnlyList<SettingsRefusal> FindRefusals(IConfiguration configuration, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);

        // The order is the composition root's rather than this file's: the owners are established before
        // `AddMailRules`, which runs before `AddPersistenceAndProviders`, which runs before the surfaces are mapped. An
        // operator whose candidate carries a mistake in two of them is shown the same one first by a write and by a
        // start, which is what the summary promises and the only thing that makes the promise worth anything.
        List<SettingsRefusal> refusals = [.. FindOwnerDeclarationRefusals(configuration, timeProvider)];

        // A group that will not bind at all raises rather than returning, and a start meeting an earlier refusal never
        // reaches it — so what is already held is what a start would have reported, and discarding it for the binder's
        // sentence would answer a two-section candidate with the second of its mistakes. Every group but the first is
        // inside the guard for that reason: the owners are established before any of them, so a candidate carrying an
        // owner mistake and a section that will not bind is answered with the owner sentence, which is the one a start
        // would have stopped at.
        try
        {
            refusals.AddRange(FindMailRuleRefusals(configuration, new NCalcMailRuleConditionCompiler()));
            refusals.AddRange(FindProviderRefusals(configuration));
            refusals.AddRange(FindSurfaceRefusals(configuration));
        }
        catch (InvalidOperationException) when (refusals.Count > 0)
        {
            return refusals;
        }

        return refusals;
    }

    /// <summary>Finds what the owners this deployment declares would be refused for.</summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <param name="timeProvider">Supplies the date the declared synchronization bounds are read against.</param>
    /// <returns>The refusal, or nothing when every declared owner could be one this deployment serves.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the collection will not bind at all.</exception>
    /// <remarks>
    /// First among the groups, because who this deployment serves decides what every other section is read for. It is
    /// composed rather than bound for the reason the surfaces are: the collection is an array at the root of the
    /// configuration, and what it decides — how many owners exist, and whose each declared mailbox is — is settled
    /// while the host is being built rather than by an options snapshot resolved later.
    /// </remarks>
    public static IReadOnlyList<SettingsRefusal> FindOwnerDeclarationRefusals(
        IConfiguration configuration,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        return Refusal<DeclaredOwnerOptions>(
            DeclaredOwnerOptions.SectionName,
            DeclaredOwners.FindConfigurationErrors(configuration, today));
    }

    /// <summary>Finds what the declared AI endpoints and the ceilings around them would be refused for.</summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <returns>One refusal per section that would stop a start, in the order a start meets them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the chat section will not bind at all, which the strict reading below is what decides.</exception>
    /// <remarks>
    /// The chat section is the one bound without <c>ValidateDataAnnotations</c> and without <c>ValidateOnStart</c>,
    /// deliberately and for the reason <see cref="BoundSettings" /> gives — it reloads, and the framework validator has
    /// nowhere to report a reloaded candidate's refusal. That makes these rules the only thing judging the section, so
    /// a candidate that escaped them would escape every rule the section has.
    /// </remarks>
    public static IReadOnlyList<SettingsRefusal> FindProviderRefusals(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var embeddings = configuration.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>();

        // The one of the three read strictly, because it is the one nothing else binds strictly for a candidate: the
        // other two are registered under ValidateOnStart, so the startup validator materializes them and reports an
        // unknown key of its own. This section is not, so a lenient read here would drop a misspelled key that a start
        // does refuse — the snapshot hosted service reads the monitor's current value, which runs the strict binder
        // while hosted services are resolved.
        var chat = configuration.GetSection(ChatModelOptions.SectionName)
            .Get<ChatModelOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true);
        var answering = configuration.GetSection(MailAnsweringOptions.SectionName).Get<MailAnsweringOptions>()
            ?? new MailAnsweringOptions();

        return
        [
            // Ahead of the chat rules because the ceiling it carries is what the filter's candidate count is judged
            // against: an operator who wrote one mistake in each reads the one their other mistake depends on first.
            .. Refusal<MailAnsweringOptions>(MailAnsweringOptions.SectionName, answering.FindConfigurationErrors()),

            // Every rule the chat declaration answers to, in one reading: the section's own bounds, the alias that names
            // one AI endpoint across the whole deployment because a credential, a resilience circuit, and a log line are
            // all keyed by it, and the filter's candidate count against what a lookup actually hands over.
            .. Refusal<ChatModelOptions>(
                ChatModelOptions.SectionName,
                ChatDeclarationRules.FindDeclarationErrors(chat, embeddings, answering)),
        ];
    }

    /// <summary>Finds what the declared rule set would be refused for.</summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <param name="conditionCompiler">The compiler every condition is read through.</param>
    /// <returns>The refusal, or nothing when every declaration compiles against an account this configuration declares.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the section will not bind at all.</exception>
    public static IReadOnlyList<SettingsRefusal> FindMailRuleRefusals(
        IConfiguration configuration,
        IMailRuleConditionCompiler conditionCompiler)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(conditionCompiler);

        var declared = configuration
            .GetSection(MailRulesOptions.SectionName)
            .Get<MailRulesOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true);

        return Refusal<MailRulesOptions>(
            MailRulesOptions.SectionName,
            MailRuleDeclarationRules.FindDeclarationErrors(
                declared,
                conditionCompiler,
                DeclaredMailAccounts.ReadFrom(configuration)));
    }

    /// <summary>Finds what the sockets, the surfaces served on them, and the postures around them would be refused for.</summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <returns>One refusal per section that would stop a start, in the order a start meets them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a section will not bind at all.</exception>
    public static IReadOnlyList<SettingsRefusal> FindSurfaceRefusals(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var reverseProxy = ReverseProxyOptions.ReadFrom(configuration);
        var connectionLimits = ConnectionLimitsOptions.ReadFrom(configuration);
        var mcp = McpEndpointOptions.ReadFrom(configuration);
        var admin = AdminEndpointOptions.ReadFrom(configuration);
        var client = ClientEndpointOptions.ReadFrom(configuration);
        var health = HealthEndpointOptions.ReadFrom(configuration);

        List<SettingsRefusal> refusals =
        [
            .. Refusal<ReverseProxyOptions>(ReverseProxyOptions.SectionName, reverseProxy.FindConfigurationErrors()),
            .. Refusal<ConnectionLimitsOptions>(ConnectionLimitsOptions.SectionName, connectionLimits.FindConfigurationErrors()),

            // Every listener this process opens is bound in code, from the section of the surface it belongs to, so the
            // host's own ways of naming one decide nothing here and are refused rather than ignored. Read at the root,
            // before any section's own errors, because an operator who stated a port that no longer binds anything needs to
            // be told that first — every message below would otherwise describe a section they had not been using.
            .. Refusal<McpEndpointOptions>(
                ExternalListenerConfiguration.KestrelEndpointsSectionName,
                ExternalListenerConfiguration.FindConfigurationErrors(configuration)),
            .. Refusal<McpEndpointOptions>(McpEndpointOptions.SectionName, FindUnservedProcessErrors(mcp, admin, client, health)),

            // Each section answers for itself, so a message about a misspelled key is not delayed behind a question about
            // a socket the deployment may not even share. The secrets they name are proven separately, by the startup
            // validator that proves every other section's.
            .. Refusal<McpEndpointOptions>(McpEndpointOptions.SectionName, mcp.FindConfigurationErrors()),
            .. Refusal<AdminEndpointOptions>(AdminEndpointOptions.SectionName, admin.FindConfigurationErrors()),
            .. Refusal<ClientEndpointOptions>(ClientEndpointOptions.SectionName, client.FindConfigurationErrors()),
            .. Refusal<HealthEndpointOptions>(HealthEndpointOptions.SectionName, health.FindConfigurationErrors()),
        ];

        // Composing the listeners is what a section's own validator has already earned the right to: declaring one reads
        // a profile as though it were valid — a domain that is unique because validation proved it so — so a section
        // that answered with a refusal is a section whose declarations cannot be built at all. The composition root
        // short-circuited on each section's errors before it ever declared a listener; a collection expression
        // evaluates every element, so a candidate carrying two colliding profiles would leave this port raising the
        // framework's key-collision message instead of returning the two sentences naming the profiles.
        if (refusals.Count > 0)
        {
            return refusals;
        }

        // Surfaces may share a socket — which is what lets a single-node deployment publish one port rather than three
        // — but they may not disagree about it, and this is where that is settled before anything binds.
        //
        // Whether a surface accepting passwords is confidential is settled here as well, and only here: it is the one
        // rule reading an endpoint's own transport against the proxy section, so neither section could hold it. Both
        // read after the short-circuit above, because each of them reads a section as though it were valid.
        return
        [
            .. Refusal<McpEndpointOptions>(
                McpEndpointOptions.SectionName,
                ListenerComposition.Compose(
                [
                    .. mcp.DeclareListeners(),
                    .. admin.DeclareListeners(),
                    .. client.DeclareListeners(),
                    .. health.DeclareListeners(),
                ]).Errors),
            .. Refusal<McpEndpointOptions>(
                McpEndpointOptions.SectionName,
                PasswordTransportConfidentiality.FindConfigurationErrors(
                    McpEndpointOptions.SectionName,
                    mcp.Enabled,
                    mcp.AllowsBasic,
                    mcp.ServesClearText,
                    reverseProxy)),
            .. Refusal<ClientEndpointOptions>(
                ClientEndpointOptions.SectionName,
                PasswordTransportConfidentiality.FindConfigurationErrors(
                    ClientEndpointOptions.SectionName,
                    client.Enabled,
                    client.AllowsBasic,
                    client.ServesClearText,
                    reverseProxy)),
        ];
    }

    /// <summary>Stops a start at the first refusal, which is what a composition root does with one.</summary>
    /// <param name="refusals">The refusals, in the order a start meets them.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusals" /> is <see langword="null" />.</exception>
    /// <exception cref="OptionsValidationException">Thrown when there is a refusal, naming the first section that carries one.</exception>
    public static void RefuseFirstOf(IReadOnlyList<SettingsRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);

        if (refusals is [var first, ..])
        {
            throw new OptionsValidationException(first.SectionName, first.SettingsType, first.Errors);
        }
    }

    /// <summary>Refuses a process that would open a socket and serve nothing on it.</summary>
    /// <remarks>
    /// A process serving none of its surfaces opens no listener at all, and Kestrel answers that by binding its own
    /// default address — and, where an ASP.NET Core development certificate happens to be installed, a TLS one beside it.
    /// That is a socket no section describes, serving whatever a route happens to match.
    /// </remarks>
    private static IReadOnlyList<string> FindUnservedProcessErrors(
        McpEndpointOptions mcp,
        AdminEndpointOptions admin,
        ClientEndpointOptions client,
        HealthEndpointOptions health) =>
        mcp.Enabled || admin.Enabled || client.Enabled || health.Enabled
            ? []
            :
            [
                $"No network surface is enabled: '{McpEndpointOptions.SectionName}:Enabled', "
                + $"'{AdminEndpointOptions.SectionName}:Enabled', '{ClientEndpointOptions.SectionName}:Enabled', and "
                + $"'{HealthEndpointOptions.SectionName}:Enabled' "
                + "are all off, so the process would serve nothing while still holding a socket. Enable the surface this "
                + "deployment exists to serve.",
            ];

    private static IReadOnlyList<SettingsRefusal> Refusal<TSettings>(string sectionName, IReadOnlyList<string> errors) =>
        errors.Count == 0 ? [] : [new SettingsRefusal(sectionName, typeof(TSettings), errors)];
}
