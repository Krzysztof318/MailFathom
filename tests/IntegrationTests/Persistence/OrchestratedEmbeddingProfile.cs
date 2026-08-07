// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Makes the geometry the deterministic generator produces the one this instance embeds into.</summary>
/// <remarks>
/// Shared and idempotent, because the profile table is unique on the identity fingerprint and every test that wants to
/// embed for free wants the same identity: a second class inserting it would fail on whichever of them xUnit happened
/// to run second. Activating an existing row is the same statement as inserting one, so asking for it twice is free.
/// </remarks>
internal static class OrchestratedEmbeddingProfile
{
    /// <summary>Ensures the deterministic generator's own geometry is registered and active, and answers with its identity.</summary>
    internal static Task<EmbeddingProfileId> EnsureActiveDeterministicAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var identity = scope.GetRequiredService<ITextEmbeddingGenerator>().Identity;
                var fingerprint = EmbeddingProfileFingerprint.Compute(identity).Value;
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var activatedAt = TimeProvider.System.GetUtcNow();

                var registered = await context.EmbeddingProfiles
                    .SingleOrDefaultAsync(profile => profile.IdentityFingerprint == fingerprint, token);

                if (registered is null)
                {
                    registered = new EmbeddingProfileEntity
                    {
                        Id = Guid.CreateVersion7(),
                        Provider = identity.Provider,
                        ModelIdentifier = identity.ModelIdentifier,
                        ModelVersion = identity.ModelVersion,
                        Dimension = identity.Dimension,
                        DistanceMetric = identity.DistanceMetric,
                        InputCharacterLimit = identity.InputPreparation.InputCharacterLimit,
                        PassageInstruction = identity.InputPreparation.PassageInstruction,
                        NormalizesVector = identity.InputPreparation.NormalizesVector,
                        IdentityFingerprint = fingerprint,
                        LifecycleState = EmbeddingProfileLifecycleState.Active,
                        RegisteredAt = activatedAt,
                        ActivatedAt = activatedAt,
                    };

                    context.EmbeddingProfiles.Add(registered);
                }
                else if (registered.LifecycleState != EmbeddingProfileLifecycleState.Active)
                {
                    registered.LifecycleState = EmbeddingProfileLifecycleState.Active;
                    registered.ActivatedAt = activatedAt;
                }

                await context.SaveChangesAsync(token);

                return EmbeddingProfileId.Create(registered.Id);
            },
            cancellationToken);
}
