// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A spend store that cannot answer, which is what a database the deployment cannot reach looks like from above the port.</summary>
/// <remarks>
/// It exists so the store's one security property can fail a test rather than only be asserted in prose: a store that
/// cannot say whether an identifier has been served must leave its caller unable to serve, and a <c>catch</c> added
/// later that answered <see langword="true" /> would otherwise pass every test written against a store that always
/// answers.
/// </remarks>
internal sealed class UnreachableClientAssertionSpendStore : IClientAssertionSpendStore
{
    /// <inheritdoc />
    public Task<bool> TrySpendAsync(
        string credentialKey,
        string identifier,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken) =>
        throw new ClientAssertionSpendUnrecordableException(
            "The assertion could not be recorded because this test's store cannot answer.",
            new InvalidOperationException("The database is unreachable."));

    /// <inheritdoc />
    public Task RemoveExpiredAsync(DateTimeOffset removableFrom, CancellationToken cancellationToken) =>
        throw new ClientAssertionSpendUnrecordableException(
            "The expired records could not be removed because this test's store cannot answer.",
            new InvalidOperationException("The database is unreachable."));
}
