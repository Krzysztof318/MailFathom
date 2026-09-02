// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Writes the stored rows a test needs before it can arrange anything about a message.</summary>
/// <remarks>
/// <see cref="SyntheticEmail" /> builds the values; this writes them. The two are separate because the builders are
/// pure and reusable wherever a value is wanted, while a write needs the composed services and a session of its own.
/// </remarks>
internal static class StoredSyntheticEmail
{
    /// <summary>Stores one occurrence's metadata and nothing else, and hands back the identifier it was given.</summary>
    /// <param name="services">The composed services the write runs through.</param>
    /// <param name="occurrence">The remote occurrence the row is written for.</param>
    /// <param name="subject">The subject, which is how a test recognizes its own row.</param>
    /// <param name="cancellationToken">Cancels the write and the commit.</param>
    /// <returns>The identifier the metadata write assigned, which is what a mutation is then requested against.</returns>
    /// <remarks>
    /// The content is declared as having exceeded the size limit, which is the one availability that states there is no
    /// raw MIME to find rather than that it has yet to arrive. That is what a test about mutations wants: the message
    /// exists, is complete as far as anything reading it is concerned, and no content store had to be seeded to say so.
    /// A test that asserts about content itself arranges its own row, since this one deliberately has none.
    /// </remarks>
    internal static Task<StoredEmailId> MetadataOnlyAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrence,
        string subject,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session, SyntheticMailAccount.Owner,
                SyntheticEmail.RemoteMetadataOf(occurrence, subject),
                extractedMetadata: null,
                StoredEmailContentAvailability.ExceededSizeLimit,
                token),
            cancellationToken);
}
