// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Cuts a stored message into passages, the way an account run's last local step does.</summary>
/// <remarks>
/// Storing a message leaves it uncut: the cut waits for classification and the rules, so it is a step of the run rather
/// than something the metadata write performs on its way past. A fixture that seeds mail directly runs neither, so a
/// test needing the passages a settled message has asks for them here — through the production store, so that what a
/// test arranges is a row shape a deployment actually produces rather than one only a fixture can.
/// </remarks>
internal static class OrchestratedPassages
{
    /// <summary>Cuts one stored message's passages and commits them.</summary>
    /// <param name="services">The orchestrated services the cut runs through.</param>
    /// <param name="storedEmailId">The message to cut.</param>
    /// <param name="cancellationToken">Cancels the cut.</param>
    /// <returns>A task that completes once the passages are durable.</returns>
    /// <remarks>
    /// Asserted rather than assumed, for the reason every other arrangement in this suite is: a test whose passages
    /// were never committed would fail on whatever it asserts about them next, which reads as the subject being wrong
    /// rather than as the arrangement never having happened.
    /// </remarks>
    internal static async Task CutAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IStoredEmailChunkingStore>().DeriveChunksAsync(
                session,
                storedEmailId,
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }
}
