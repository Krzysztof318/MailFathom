// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;
using MailFathom.Host.Configuration.Endpoints;
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
    /// <returns>One refusal per section that would stop a start, empty when none would.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a section will not bind at all, which stops the reading where it stood.</exception>
    public static IReadOnlyList<SettingsRefusal> FindRefusals(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return
        [
            .. FindMailRuleRefusals(configuration, new NCalcMailRuleConditionCompiler()),
            .. FindSurfaceRefusals(configuration),
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

        return
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

            // Surfaces may share a socket — which is what lets a single-node deployment publish one port rather than three
            // — but they may not disagree about it, and this is where that is settled before anything binds.
            .. Refusal<McpEndpointOptions>(
                McpEndpointOptions.SectionName,
                ListenerComposition.Compose(
                [
                    .. mcp.DeclareListeners(),
                    .. admin.DeclareListeners(),
                    .. client.DeclareListeners(),
                    .. health.DeclareListeners(),
                ]).Errors),
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
