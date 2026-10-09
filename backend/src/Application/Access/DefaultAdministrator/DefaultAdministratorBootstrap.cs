// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.DefaultAdministrator;

/// <summary>Records a deployment's default administrator on its first start, and gives it the password the deployment was started with.</summary>
/// <remarks>
/// <para>
/// The first start records a user <c>admin</c>, in no organization, holding the seeded <c>Administrator</c> role at the
/// deployment scope, with both of its mail endpoint switches off so no MCP or client caller is ever <c>admin</c>. It
/// happens once per deployment: removing <c>admin</c> afterwards is final, because a deployment whose operator removed
/// the default account and then found it back with a known password would be worse off than one that never had it.
/// </para>
/// <para>
/// The password setting is applied at most once per deployment. The first start that finds it while <c>admin</c> holds
/// no password credential provisions one, presented on the administrative endpoint alone, and records that the setting
/// was applied; every later start ignores it, whatever happened to that credential since. So changing the setting does
/// not change the password, and no route that removes or disables <c>admin</c>'s credential can turn the next restart
/// into a sign-in with the shipped value.
/// </para>
/// <para>
/// The shipped value <c>admin</c> is exempt from the password policy's minimum and nothing else is: a value printed in
/// every deployment asset is public whatever its length, while any other value is one an operator chose and is held to
/// the policy like every password. While the stored password still verifies against the shipped value, every start
/// reports it.
/// </para>
/// </remarks>
public sealed class DefaultAdministratorBootstrap
{
    /// <summary>The username the default administrator signs in with, in no organization.</summary>
    public const string Username = "admin";

    /// <summary>The password every copy of the deployment assets that sets one ships with.</summary>
    public const string ShippedPassword = "admin";

    private readonly AccessAuthorization authorization;
    private readonly IDefaultAdministratorStore store;
    private readonly IUserCredentialStore credentials;
    private readonly IPasswordHasher passwordHasher;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="DefaultAdministratorBootstrap" /> class.</summary>
    /// <param name="authorization">Admits the process identity, which is the only principal a start runs under.</param>
    /// <param name="store">Where the record of the administrator and of the applied setting is kept.</param>
    /// <param name="credentials">Reads the administrator's password credential back, to tell whether it is still the shipped one.</param>
    /// <param name="passwordHasher">Hashes the setting, and verifies the stored password against the shipped value.</param>
    /// <param name="timeProvider">Supplies the instant each record is written at.</param>
    public DefaultAdministratorBootstrap(
        AccessAuthorization authorization,
        IDefaultAdministratorStore store,
        IUserCredentialStore credentials,
        IPasswordHasher passwordHasher,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.store = store;
        this.credentials = credentials;
        this.passwordHasher = passwordHasher;
        this.timeProvider = timeProvider;
    }

    /// <summary>Records the default administrator where this deployment never did, and applies the password setting where it never was.</summary>
    /// <param name="passwordSetting">The value the deployment was started with, or <see langword="null" /> where it carried none.</param>
    /// <param name="settingName">The environment variable the value was read from, which a refusal names.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>What the start established, which the startup record reports.</returns>
    /// <exception cref="DefaultAdministratorUnusableException">Thrown when the value fails the password policy, or when the username belongs to another user's credential.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when reached under any principal but the process identity.</exception>
    public async Task<DefaultAdministratorStart> StartAsync(
        string? passwordSetting,
        string settingName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);

        this.authorization.RequireProcessIdentity();

        var password = string.IsNullOrEmpty(passwordSetting) ? null : passwordSetting;
        var now = this.timeProvider.GetUtcNow();
        var record = await this.store.RecordOnceAsync(
            UserId.Create(Guid.CreateVersion7(now)),
            Guid.CreateVersion7(now),
            now,
            cancellationToken);

        if (record.Administrator is not { } administrator)
        {
            return new DefaultAdministratorStart(Administrator: null, PasswordSetting: null, SignsInWithShippedPassword: false);
        }

        var applied = password is null || record.PasswordSettingApplied
            ? (DefaultAdministratorPasswordOutcome?)null
            : await this.ApplyAsync(administrator, password, settingName, now, cancellationToken);

        return new DefaultAdministratorStart(
            administrator,
            applied,
            await this.SignsInWithShippedPasswordAsync(administrator, cancellationToken));
    }

    /// <summary>Gives the default administrator the password the setting carries, refusing a value the policy refuses.</summary>
    /// <remarks>The policy is asked here rather than on every start, because a setting already applied is ignored whatever it now holds, and a value nobody will apply is no reason to stop a start.</remarks>
    private async Task<DefaultAdministratorPasswordOutcome> ApplyAsync(
        UserId administrator,
        string password,
        string settingName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (password != ShippedPassword && UserPasswordPolicy.FindRefusal(password) is { } refusal)
        {
            throw DefaultAdministratorUnusableException.PasswordRefused(settingName, refusal);
        }

        var outcome = await this.store.ApplyPasswordSettingAsync(
            administrator,
            Guid.CreateVersion7(now),
            UserCredentialLookup.ForUsername(UserCredentialUsername.Create(Username)),
            this.passwordHasher.Hash(password),
            MailFathomPermission.PublishedFor(ProtectedSurface.Mail),
            now,
            cancellationToken);

        return outcome == DefaultAdministratorPasswordOutcome.UsernameTaken
            ? throw DefaultAdministratorUnusableException.UsernameTaken()
            : outcome;
    }

    private async Task<bool> SignsInWithShippedPasswordAsync(UserId administrator, CancellationToken cancellationToken)
    {
        if (!UserCredentialLogin.TryRead(Username, out var login))
        {
            return false;
        }

        var credential = await this.credentials.FindPasswordAsync(login, cancellationToken);

        return credential is { Enabled: true, Material: { } stored }
            && credential.User == administrator
            && this.passwordHasher.Verify(stored, ShippedPassword) != PasswordVerification.Failed;
    }
}
