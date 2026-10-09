// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers the reader a backfill walks mail of many accounts through, each read against its own account's settings.</summary>
public sealed class AccountScopedEmailMimeReaderTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Home = MailAccountId.Create("home");

    private readonly MailSynchronizationOptions forWork = new();

    private readonly MailSynchronizationOptions forHome = new();

    private readonly List<MailSynchronizationOptions> composedAgainst = [];

    /// <summary>
    /// What a message is read with depends on its account's settings, so mail of two accounts is read through two
    /// prepared scopes, and a second message of the same account reuses its scope rather than reading the record again.
    /// </summary>
    [Fact]
    public async Task ReadMetadataAsync_MailOfTwoAccounts_ReadsEachAgainstItsOwnAccountsSettings()
    {
        // Arrange
        await using var services = this.Services();
        await using var reader = new AccountScopedEmailMimeReader(services.GetRequiredService<IServiceScopeFactory>());

        // Act
        await reader.ReadMetadataAsync(Work, ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken);
        await reader.ReadMetadataAsync(Home, ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken);
        await reader.ReadMetadataAsync(Work, ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([this.forWork, this.forHome], this.composedAgainst);
    }

    private ServiceProvider Services()
    {
        var accounts = Substitute.For<IMailSynchronizationAccountSource>();
        accounts.ReadRunSettingsAsync(Work, null, Arg.Any<CancellationToken>()).Returns(this.forWork);
        accounts.ReadRunSettingsAsync(Home, null, Arg.Any<CancellationToken>()).Returns(this.forHome);

        return new ServiceCollection()
            .AddScoped(_ => new ScopedMailSynchronizationSettings(
                new StubSettingsSnapshot<MailSynchronizationOptions>(new MailSynchronizationOptions()),
                accounts))
            .AddScoped(provider =>
            {
                this.composedAgainst.Add(provider.GetRequiredService<ScopedMailSynchronizationSettings>().Current);

                return Substitute.For<IEmailMimeReader>();
            })
            .BuildServiceProvider();
    }
}
