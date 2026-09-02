// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Leaves on a stored message what an account run's rule pass leaves on one it has finished with.</summary>
/// <remarks>
/// Every path that cuts passages waits for that stamp, because the rules are the stage that may still move a message
/// out of the folder its passages would describe. A fixture that seeds mail directly never runs a rule pass, so without
/// this the mail it seeds is mail no cut is meant to reach — and a test asserting that something cut it would be
/// asserting the ordering is broken. Written as a set-based update rather than through the pass itself, because what a
/// fixture needs is the state the pass leaves rather than a second run of it.
/// </remarks>
internal static class OrchestratedRuleEvaluationStamp
{
    /// <summary>Marks one stored message as evaluated, at the instant the caller names.</summary>
    /// <param name="services">The orchestrated services the update runs through.</param>
    /// <param name="storedEmailId">The message to stamp.</param>
    /// <param name="evaluatedAt">When the rules are recorded as having finished with it.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row carries the stamp.</returns>
    internal static Task ApplyAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken) =>
        StampAsync(services, storedEmailId, evaluatedAt, cancellationToken);

    /// <summary>Takes the stamp off again, leaving a message the rules have not reached.</summary>
    /// <param name="services">The orchestrated services the update runs through.</param>
    /// <param name="storedEmailId">The message to leave unevaluated.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row carries no stamp.</returns>
    /// <remarks>
    /// Cheaper than seeding a second way: a fixture stamps what it stores so its ordinary mail is cuttable, and a test
    /// about the waiting itself takes the stamp back off one message rather than the helper growing a switch every
    /// caller then has to answer.
    /// </remarks>
    internal static Task ClearAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) =>
        StampAsync(services, storedEmailId, evaluatedAt: null, cancellationToken);

    private static Task<int> StampAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        DateTimeOffset? evaluatedAt,
        CancellationToken cancellationToken) => services.InScopeAsync(
        (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
            .Where(email => email.Id == storedEmailId.Value)
            .ExecuteUpdateAsync(setters => setters.SetProperty(email => email.RulesEvaluatedAt, evaluatedAt), token),
        cancellationToken);
}
