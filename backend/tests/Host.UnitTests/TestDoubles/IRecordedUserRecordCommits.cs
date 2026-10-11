// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>What a user record's commit was composed to, as a test states its expectation of it.</summary>
/// <remarks>
/// The writer a record is committed through takes a function, because the record is composed under the settings
/// policies in force inside the commit. A test cannot match on a function, so <see cref="ComposingUserRecordWriter" />
/// runs it and reports what it composed here: the document, the endpoint switches it resolves to, and the version it
/// was composed over. A substitute of this is what a test arranges an answer on and asserts a commit against, which
/// is why it is public: the proxy a substitute is built from cannot reach an internal type of this assembly.
/// </remarks>
public interface IRecordedUserRecordCommits
{
    /// <summary>Records one composed commit and answers the version it produced.</summary>
    /// <param name="user">The user whose record was committed.</param>
    /// <param name="json">The record's own document as it was composed.</param>
    /// <param name="endpointAccess">The switches the record resolves to under the policies it was composed under.</param>
    /// <param name="expectedVersion">The version the change was composed over.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns>The version the commit produced, or <see langword="null" /> where the test arranged a commit that does not land.</returns>
    Task<long?> CommitAsync(
        UserId user,
        string json,
        UserEndpointAccess endpointAccess,
        long expectedVersion,
        CancellationToken cancellationToken);
}
