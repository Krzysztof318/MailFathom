// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Sockets;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.MailKit.Delivery;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Builds the account, policy, client, and factory the SMTP delivery adapter tests arrange around.</summary>
/// <remarks>
/// The client is the interface MailKit publishes rather than a hand-written copy of it, so a test scripts a submission
/// server by saying what that server advertises and how it answers. The transport underneath is a delegate, which is
/// what keeps a unit test off the network while leaving the two stages the adapter separates observable.
/// </remarks>
internal static class SmtpDeliveryTestContext
{
    /// <summary>The account every delivery test opens a session for.</summary>
    internal static MailAccountId Account { get; } = MailAccountId.Create("primary");

    /// <summary>The host the scripted submission endpoint answers on.</summary>
    internal const string SubmissionHost = "smtp.example.test";

    /// <summary>The port the scripted submission endpoint answers on.</summary>
    internal const int SubmissionPort = 465;

    /// <summary>A policy that reaches the endpoint over implicit TLS and permits one clear-text mechanism.</summary>
    internal static MailTransportSecurityPolicy TlsOnConnectWithPlainPolicy { get; } =
        MailKitImapSessionTestContext.CreatePolicy(MailConnectionSecurity.TlsOnConnect, MailAuthenticationMechanism.Plain);

    /// <summary>A policy that permits only the registered token-bearing mechanism, so no password path is reachable.</summary>
    internal static MailTransportSecurityPolicy TlsOnConnectWithOAuthBearerPolicy { get; } =
        MailKitImapSessionTestContext.CreatePolicy(MailConnectionSecurity.TlsOnConnect, MailAuthenticationMechanism.OAuthBearer);

    /// <summary>Builds a pipeline that makes exactly one attempt, so a test about adapter behavior observes one call.</summary>
    internal static OutboundResilienceTestHost CreateSingleAttemptResilience() =>
        OutboundResilienceTestHost.WithConfiguredSettings(
            ("EmailDelivery:MaxAttempts", "1"),
            ("EmailDelivery:AttemptTimeout", "00:10:00"),
            ("EmailDelivery:TotalTimeout", "00:20:00"));

    /// <summary>Builds a scripted submission server that advertises the supplied SASL mechanisms.</summary>
    /// <param name="advertisedMechanisms">The mechanisms the server offers, as it would in its greeting.</param>
    /// <returns>The client, whose capabilities a test then sets to whatever the server declares.</returns>
    internal static ISubmissionClient CreateClient(params string[] advertisedMechanisms)
    {
        var client = Substitute.For<ISubmissionClient>();
        client.AuthenticationMechanisms.Returns([.. advertisedMechanisms]);

        return client;
    }

    /// <summary>Scripts how a submission server answers the access tokens presented to it, and records each one.</summary>
    /// <param name="client">The scripted server.</param>
    /// <param name="refusedAuthenticationCount">How many of the first tokens the server refuses, modelling one it no longer accepts.</param>
    /// <returns>The access token presented by every token authentication attempted, in order.</returns>
    /// <remarks>
    /// The presented value is recorded rather than counted, because what the renewal path has to prove is that the
    /// second attempt carried a different token: a re-authentication repeating the refused one would pass a test that
    /// only counted the round trips.
    /// </remarks>
    internal static IReadOnlyList<string> ScriptTokenAuthentication(
        ISubmissionClient client,
        int refusedAuthenticationCount = 0)
    {
        ArgumentNullException.ThrowIfNull(client);

        var presentedAccessTokens = new List<string>();

        client.AuthenticateAsync(Arg.Any<SaslMechanism>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var mechanism = call.Arg<SaslMechanism>()
                ?? throw new InvalidOperationException("The adapter authenticated with no SASL mechanism.");

            presentedAccessTokens.Add(mechanism.Credentials?.Password ?? string.Empty);

            // Counted against the attempts already recorded, so "refuse the first one" reads as exactly that.
            return presentedAccessTokens.Count <= refusedAuthenticationCount
                ? throw new AuthenticationException("The submission server refused the access token.")
                : Task.CompletedTask;
        });

        return presentedAccessTokens;
    }

    /// <summary>Builds a settings provider that resolves a password for the scripted endpoint on every attempt.</summary>
    internal static ISmtpAccountSettingsProvider CreateSettingsProvider() => CreateSettingsProvider(out _);

    /// <summary>Builds a settings provider and reports the material each attempt resolved, so its erasure can be asserted.</summary>
    internal static ISmtpAccountSettingsProvider CreateSettingsProvider(
        out List<MailAccountConnectionMaterial> resolvedMaterial)
    {
        var issuedMaterial = new List<MailAccountConnectionMaterial>();
        var settingsProvider = Substitute.For<ISmtpAccountSettingsProvider>();
        settingsProvider.GetSettingsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            var accountId = callInfo.Arg<string>() ?? Account.Value;
            var material = new MailAccountConnectionMaterial(
                ResolvedSecret.FromText("password"),
                TrustedCertificateAuthority: null);
            issuedMaterial.Add(material);

            return Task.FromResult(new SmtpAccountSettings(
                accountId,
                SubmissionHost,
                SubmissionPort,
                "user",
                material));
        });

        resolvedMaterial = issuedMaterial;

        return settingsProvider;
    }

    /// <summary>Builds a factory over one scripted client, the real classifier, and the host's controllable clock.</summary>
    /// <remarks>
    /// The transport is a parameter rather than something built here, because every attempt it serves allocates a
    /// socket that only its owner can release: a test holds it for as long as it holds the factory. A test about a
    /// transport that never opens supplies <paramref name="socketConnector" /> instead, which hands out no socket at
    /// all and therefore owns nothing.
    /// </remarks>
    internal static MailKitSmtpDeliverySessionFactory CreateFactory(
        OutboundResilienceTestHost resilience,
        ISubmissionClient client,
        ScriptedSubmissionTransport transport,
        ISmtpAccountSettingsProvider? settingsProvider = null,
        IMailAccessTokenSource? accessTokenSource = null,
        MailDeliveryTimeouts? timeouts = null,
        Func<string, int, CancellationToken, Task<Socket>>? socketConnector = null) =>
        new(
            () => client,
            socketConnector ?? transport.ConnectAsync,
            settingsProvider ?? CreateSettingsProvider(),
            accessTokenSource ?? new UnusedMailAccessTokenSource(),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            timeouts ?? MailDeliveryTimeouts.Default,
            new MailDeliveryTelemetry(resilience.TimeProvider),
            resilience.TimeProvider,
            resilience.Services.GetRequiredService<ILoggerFactory>().CreateLogger<MailKitSmtpConnection>());
}
