// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers the step that gives a request the mail settings of the user it acts for, before anything inside it reads them.</summary>
public sealed class ActingUserMailSettingsTests
{
    private readonly MailSynchronizationOptions published = new();

    private readonly MailSynchronizationOptions forUser = new();

    private readonly IMailSynchronizationAccountSource accounts = Substitute.For<IMailSynchronizationAccountSource>();

    private readonly IAuthorizedPrincipalSource principals = Substitute.For<IAuthorizedPrincipalSource>();

    public ActingUserMailSettingsTests() =>
        this.accounts.ReadUserSettingsAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns(this.forUser);

    [Fact]
    public async Task PrepareAsync_ARequestActingForAUser_ServesItThatUsersSettingsBeforeTheNextStepRuns()
    {
        // Arrange
        this.principals.Current.Returns(AuthorizedPrincipal.SignedCapability(SyntheticUser.Deployment, "attachment"));

        // Act
        var seen = await this.SettingsTheNextStepSeesAsync();

        // Assert
        Assert.Same(this.forUser, seen);
    }

    /// <summary>A caller acting for nobody — an operator, or the process itself — reads no user's record and keeps the published settings.</summary>
    [Fact]
    public async Task PrepareAsync_ARequestActingForNobody_LeavesThePublishedSettings()
    {
        // Arrange
        this.principals.Current.Returns(AuthorizedPrincipal.Process);

        // Act
        var seen = await this.SettingsTheNextStepSeesAsync();

        // Assert
        Assert.Same(this.published, seen);
        await this.accounts.DidNotReceive().ReadUserSettingsAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A caller whose user cannot be settled is refused by the use case it reaches, which states why; preparing here
    /// would answer that same request with a failure that names nothing.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_ACallerWhoseUserCannotBeSettled_LeavesTheRefusalToTheUseCase()
    {
        // Arrange
        this.principals.Current.Returns(_ => throw DeploymentUserUnresolvedException.NoSoleUserToActFor());

        // Act
        var seen = await this.SettingsTheNextStepSeesAsync();

        // Assert
        Assert.Same(this.published, seen);
    }

    private async Task<MailSynchronizationOptions> SettingsTheNextStepSeesAsync()
    {
        await using var services = new ServiceCollection()
            .AddSingleton(this.principals)
            .AddScoped(_ => new ScopedMailSynchronizationSettings(
                new StubSettingsSnapshot<MailSynchronizationOptions>(this.published),
                this.accounts))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        MailSynchronizationOptions? seen = null;

        await ActingUserMailSettings.PrepareAsync(
            context,
            next =>
            {
                seen = next.RequestServices.GetRequiredService<ScopedMailSynchronizationSettings>().Current;

                return Task.CompletedTask;
            });

        return seen!;
    }
}
