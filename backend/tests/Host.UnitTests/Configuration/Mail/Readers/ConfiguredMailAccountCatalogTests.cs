// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail.Readers;

/// <summary>Covers how the deployment's served accounts are answered from the records PostgreSQL holds.</summary>
public sealed class ConfiguredMailAccountCatalogTests
{
    private const string AlexAtWork = "0199a0c0-0000-7000-8000-00000000000a";

    private const string MorganAtWork = "0199a0c0-0000-7000-8000-00000000000b";

    /// <summary>The records are the source a reader of the whole set is answered from, so each row becomes the account it names.</summary>
    [Fact]
    public async Task ReadServedAccountsAsync_ServedRows_AnswersEachAsTheAccountItNamesInOrdinalOrder()
    {
        // Arrange
        var later = new ServedMailAccountRow(new Guid(MorganAtWork), "Morgan at work", MailSynchronizationMode.Push);
        var earlier = new ServedMailAccountRow(new Guid(AlexAtWork), "Alex at work", MailSynchronizationMode.Polling);
        var catalog = CatalogReading(later, earlier);

        // Act
        var served = await catalog.ReadServedAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (AlexAtWork, "Alex at work", MailSynchronizationMode.Polling),
                (MorganAtWork, "Morgan at work", MailSynchronizationMode.Push),
            ],
            served.Select(account => (account.Id.Value, account.DisplayName.Value, account.SynchronizationMode)));
    }

    /// <summary>A row whose name cannot be shown is left out of the set, as the composed roster leaves it out.</summary>
    [Fact]
    public async Task ReadServedAccountsAsync_ARowWhoseDisplayNameIsUnusable_IsLeftOut()
    {
        // Arrange
        var catalog = CatalogReading(
            new ServedMailAccountRow(new Guid(AlexAtWork), "Alex at work", MailSynchronizationMode.Polling),
            new ServedMailAccountRow(new Guid(MorganAtWork), "Morgan\u0007at work", MailSynchronizationMode.Polling));

        // Act
        var served = await catalog.ReadServedAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([AlexAtWork], served.Select(account => account.Id.Value));
    }

    /// <summary>A reader of one account is answered from that account's row alone, never from the whole set.</summary>
    [Fact]
    public async Task ReadServedAccountsAsync_AmongNamedAccounts_ReadsOnlyThoseRows()
    {
        // Arrange
        var reader = Substitute.For<IServedMailAccountReader>();
        reader.ReadServedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ServedMailAccountRow(new Guid(MorganAtWork), "Morgan at work", MailSynchronizationMode.Push)]);
        var catalog = new ConfiguredMailAccountCatalog(Synchronizing(), reader);

        // Act
        var served = await catalog.ReadServedAccountsAsync(
            [MailAccountId.Create(MorganAtWork)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MorganAtWork], served.Select(account => account.Id.Value));
        await reader.Received(1).ReadServedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(among => among!.SequenceEqual(new[] { new Guid(MorganAtWork) })),
            Arg.Any<CancellationToken>());
        await reader.DidNotReceive().ReadServedAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>An identifier this deployment never generates names no record, so it is answered as unserved without a read.</summary>
    [Fact]
    public async Task ReadServedAccountsAsync_AmongIdentifiersNamingNoRecord_AnswersNothingWithoutReading()
    {
        // Arrange
        var reader = Substitute.For<IServedMailAccountReader>();
        var catalog = new ConfiguredMailAccountCatalog(Synchronizing(), reader);

        // Act
        var served = await catalog.ReadServedAccountsAsync(
            [MailAccountId.Create("alex-work")],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(served);
        await reader.DidNotReceive().ReadServedAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    private static ConfiguredMailAccountCatalog CatalogReading(params ServedMailAccountRow[] rows)
    {
        var reader = Substitute.For<IServedMailAccountReader>();
        reader.ReadServedAsync(Arg.Any<CancellationToken>()).Returns(rows);

        return new ConfiguredMailAccountCatalog(Synchronizing(), reader);
    }

    private static MailSynchronizationOptions Synchronizing() => new() { Enabled = true };
}
