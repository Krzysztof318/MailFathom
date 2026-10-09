// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>An administrative request whose services an endpoint resolves once it has prepared one account's settings.</summary>
/// <remarks>
/// The settings answer for exactly the accounts named, so an account outside them reads as one whose record is gone by
/// the time the request prepares it. The services are resolved from the request only, which is what lets a test see
/// that an endpoint asked for them after preparing rather than before.
/// </remarks>
internal sealed class AccountScopedRequest : IDisposable
{
    private readonly ServiceProvider services;

    private AccountScopedRequest(ServiceProvider services, ScopedMailSynchronizationSettings mailSettings)
    {
        this.services = services;
        this.MailSettings = mailSettings;
        this.Context = new DefaultHttpContext { RequestServices = services };
    }

    /// <summary>Gets the settings the endpoint prepares.</summary>
    internal ScopedMailSynchronizationSettings MailSettings { get; }

    /// <summary>Gets the request whose services the endpoint resolves.</summary>
    internal HttpContext Context { get; }

    /// <summary>Builds a request whose settings answer for the accounts named and whose services are the ones registered.</summary>
    /// <param name="register">Registers what the endpoint resolves from the request.</param>
    /// <param name="servedAccounts">The accounts whose settings can be prepared.</param>
    /// <returns>The request, which the caller disposes.</returns>
    internal static AccountScopedRequest Serving(Action<IServiceCollection> register, params MailAccountId[] servedAccounts)
    {
        ArgumentNullException.ThrowIfNull(register);

        var collection = new ServiceCollection();
        register(collection);

        var settings = new MailSynchronizationOptions();
        var accounts = Substitute.For<IMailSynchronizationAccountSource>();
        accounts.ReadRunSettingsAsync(Arg.Any<MailAccountId>(), Arg.Any<MailSynchronizationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(servedAccounts.Contains(call.ArgAt<MailAccountId>(0)) ? settings : null));

        return new AccountScopedRequest(
            collection.BuildServiceProvider(),
            new ScopedMailSynchronizationSettings(Substitute.For<ISettingsSnapshot<MailSynchronizationOptions>>(), accounts));
    }

    /// <inheritdoc />
    public void Dispose() => this.services.Dispose();
}
