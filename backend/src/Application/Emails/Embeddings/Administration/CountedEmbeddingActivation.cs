// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.Embeddings.Administration;

/// <summary>Counts what activating the declared geometry would cost, and refuses it where the budget does not admit it.</summary>
/// <remarks>
/// <para>
/// <see cref="EmbeddingProfileActivation" /> is the writer and deliberately knows nothing about money: by the time it
/// is called the decision has been made. This is the step in front of it that makes the decision an informed one — the
/// counting, the ceiling, and the refusal that
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// puts between an operator and the first thing MailFathom does that costs money per unit of mail.
/// </para>
/// <para>
/// The confirmation itself is not here. Whether a person agreed is a property of the terminal the command was typed at,
/// so it belongs to <c>mfctl</c>; what belongs here is the number they are agreeing to and the ceiling they already
/// agreed to, both of which are facts about the deployment.
/// </para>
/// </remarks>
public sealed class CountedEmbeddingActivation
{
    private readonly IEmbeddingGenerationStore generationStore;
    private readonly IEmbeddingWorkloadReader workloadReader;
    private readonly EmbeddingSpendGate spendGate;
    private readonly EmbeddingProfileActivation activation;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes a new counted activation.</summary>
    /// <param name="generationStore">Reads which generations exist, which decides what an activation would do.</param>
    /// <param name="workloadReader">Counts the passages the run would send.</param>
    /// <param name="spendGate">Reads where the budget period stands.</param>
    /// <param name="activation">Performs the activation once it has been weighed.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public CountedEmbeddingActivation(
        IEmbeddingGenerationStore generationStore,
        IEmbeddingWorkloadReader workloadReader,
        EmbeddingSpendGate spendGate,
        EmbeddingProfileActivation activation,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(generationStore);
        ArgumentNullException.ThrowIfNull(workloadReader);
        ArgumentNullException.ThrowIfNull(spendGate);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(authorization);

        this.generationStore = generationStore;
        this.workloadReader = workloadReader;
        this.spendGate = spendGate;
        this.activation = activation;
        this.authorization = authorization;
    }

    /// <summary>Reads what activating the declared geometry would do and what it would cost, writing nothing.</summary>
    /// <param name="declared">The geometry configuration declares.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The assessment an operator confirms against.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declared" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>Reading what an activation would cost is a report about this deployment, so it asks for the read permission and never for the one that starts a bill.</remarks>
    public Task<EmbeddingActivationAssessment> AssessAsync(
        EmbeddingProfileIdentity declared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declared);

        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.ReadAssessmentAsync(declared, cancellationToken);
    }

    /// <summary>Composes the assessment, having established that whoever asked may have it.</summary>
    /// <remarks>
    /// Separated from <see cref="AssessAsync" /> because the activation below weighs the same figures and is reached
    /// under a different grant. No permission implies another here as anywhere else, so an activating caller reading
    /// through the assessing method would be refused for lacking a permission its own operation never needed.
    /// </remarks>
    private async Task<EmbeddingActivationAssessment> ReadAssessmentAsync(
        EmbeddingProfileIdentity declared,
        CancellationToken cancellationToken)
    {
        var declaredFingerprint = EmbeddingProfileFingerprint.Compute(declared);
        var generations = await this.generationStore.ReadGenerationsAsync(cancellationToken);
        var estimate = await this.workloadReader.ReadWorkloadAsync(declaredFingerprint, cancellationToken);
        var period = await this.spendGate.ReadCurrentPeriodAsync(cancellationToken);

        return new EmbeddingActivationAssessment(
            declared,
            Forecast(generations, declaredFingerprint),
            estimate,
            period);
    }

    /// <summary>Weighs the declared geometry and activates it unless the spend ceiling refuses.</summary>
    /// <param name="declared">The geometry configuration declares.</param>
    /// <param name="cancellationToken">Cancels the reads and the registration.</param>
    /// <returns>What was weighed, and what the activation did where it ran.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declared" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminSpend" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The assessment is taken again here rather than accepted from the caller, so what refuses a spend is the state of
    /// the deployment at the moment it would happen rather than a figure a client read earlier and may have edited on
    /// the way back. Every other failure of the activation itself — a lost registration race above all — is
    /// <see cref="EmbeddingProfileActivation" />'s and reaches the caller unchanged.
    /// <para>
    /// This is the one operation on the administrative surface that starts a provider bill, which is why it is the only
    /// one asking for <see cref="MailFathomPermission.AdminSpend" /> and why holding the read permission does not reach
    /// it.
    /// </para>
    /// </remarks>
    public async Task<CountedEmbeddingActivationResult> ActivateAsync(
        EmbeddingProfileIdentity declared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declared);

        this.authorization.RequirePermission(MailFathomPermission.AdminSpend);

        var assessment = await this.ReadAssessmentAsync(declared, cancellationToken);

        if (assessment.ExceedsSpendCeiling)
        {
            return new CountedEmbeddingActivationResult(assessment, Activation: null);
        }

        return new CountedEmbeddingActivationResult(
            assessment,
            await this.activation.ActivateAsync(declared, cancellationToken));
    }

    /// <summary>Places the declared geometry against the generations this instance holds.</summary>
    /// <remarks>
    /// Compared through the fingerprint for the reason the activation itself compares through it: that digest is what
    /// the profile table is unique on, so agreeing on it is the same statement as resolving to the same row.
    /// </remarks>
    private static EmbeddingActivationForecast Forecast(
        EmbeddingGenerations generations,
        EmbeddingProfileFingerprint declared)
    {
        if (generations.Serving is { } serving && Fingerprints(serving) == declared)
        {
            return EmbeddingActivationForecast.AlreadyServing;
        }

        if (generations.Building is not { } building)
        {
            return EmbeddingActivationForecast.WouldStartReindex;
        }

        return Fingerprints(building) == declared
            ? EmbeddingActivationForecast.WouldResumeReindex
            : EmbeddingActivationForecast.DifferentReindexRunning;
    }

    private static EmbeddingProfileFingerprint Fingerprints(RegisteredEmbeddingProfile profile) =>
        EmbeddingProfileFingerprint.Compute(profile.Identity);
}
