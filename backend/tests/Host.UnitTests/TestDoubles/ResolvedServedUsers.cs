// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Records;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the served-user cache of a deployment already holding the users a test names, so a test downstream of it need not compose one.</summary>
/// <remarks>
/// Every user is held as though their record had just been committed at version one, and the rows behind them answer
/// that same version, so a comparison finds nothing to recompose. A user the test did not name is read from no record
/// and is therefore served nothing.
/// </remarks>
internal static class ResolvedServedUsers
{
    /// <summary>Builds the cache of a deployment whose one user holds the mailboxes given.</summary>
    /// <param name="user">The user the accounts belong to.</param>
    /// <param name="displayName">The label the user is recorded under.</param>
    /// <param name="mailAccounts">The mail accounts assigned to that user.</param>
    /// <returns>The cache, holding that one user.</returns>
    internal static ServedUsers Recording(
        UserId user,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        Serving(new ServedUser(user, displayName, mailAccounts));

    /// <summary>Builds a cache holding the users it is given.</summary>
    /// <param name="users">The users the deployment serves.</param>
    /// <returns>The cache.</returns>
    internal static ServedUsers Serving(params ServedUser[] users)
    {
        var documents = Substitute.For<IUserSettingsDocumentReader>();
        IReadOnlyList<UserSettingsDocumentVersion> versions =
            [.. users.Select(static served => new UserSettingsDocumentVersion(served.User, 1))];

        documents.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<UserSettingsDocumentVersion>>([.. versions.Take(callInfo.ArgAt<int>(0))]));
        documents.ReadVersionsAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(versions));

        var servedUsers = Over(documents);

        foreach (var served in users)
        {
            servedUsers.Published(served, 1);
        }

        return servedUsers;
    }

    /// <summary>Builds an empty cache reading every user from the records the given reader answers with.</summary>
    /// <param name="documents">The records and versions behind the cache.</param>
    /// <param name="timeProvider">The clock a held user is let go by, or a fixed one where the test does not care.</param>
    /// <param name="heldBackRecords">What the cache reports a record it will not read to, or a fresh register where the test does not ask.</param>
    /// <param name="log">What composing a user reports, or nowhere where the test does not ask.</param>
    /// <returns>The cache, holding nobody until something asks.</returns>
    internal static ServedUsers Over(
        IUserSettingsDocumentReader documents,
        TimeProvider? timeProvider = null,
        HeldBackRecords? heldBackRecords = null,
        ILogger<ServedUserResolution>? log = null)
    {
        heldBackRecords ??= new HeldBackRecords();

        var binder = new UserAccountDocumentBinder(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider(),
            Options.Create(new SensitiveContentOptions()));
        var resolution = new ServedUserResolution(
            documents,
            new ServedUserRecordComposition(binder),
            SecretValidation.OverRegisteredSchemes(),
            heldBackRecords,
            log ?? NullLogger<ServedUserResolution>.Instance);

        return new ServedUsers(documents, resolution, heldBackRecords, timeProvider ?? new FakeTimeProvider());
    }
}
