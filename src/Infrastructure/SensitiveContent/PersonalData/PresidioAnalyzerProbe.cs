// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net.Http.Json;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.SensitiveContent.PersonalData;

/// <summary>Asks the analyzer what it can recognise, so a deployment that cannot scan does not start.</summary>
/// <remarks>
/// <para>
/// The probe asks for the entities the analyzer supports in the configured language rather than for its health, and the
/// difference is what it establishes. A health route answers whether a process is listening; this answers whether the
/// process listening is an analyzer, whether it has a model for the language every request will state, and whether the
/// recognizers behind the switched-on categories are loaded. All three fail the same way at run time — no findings — and
/// no findings is indistinguishable from a clean message.
/// </para>
/// <para>
/// It is deliberately not a repeated check. Once the host has come up the scanner's own fail-closed contract covers an
/// analyzer that disappears afterwards, and a periodic probe would be a second opinion about the same dependency that no
/// caller reads.
/// </para>
/// <para>
/// Marked beside the scanner, and for the same reason: what this class claims is that the entities MailFathom's categories
/// are made of are ones the shipped analyzer actually registers. A scripted registry proves that the comparison works
/// against the registry somebody hand-wrote, which is the one thing that cannot go wrong in a deployment.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PresidioAnalyzerProbe : IPersonalDataAnalyzerProbe
{
    /// <summary>The analyzer route that answers which entities it can recognise, without its language argument.</summary>
    private const string SupportedEntitiesRoute = "supportedentities";

    private readonly Uri supportedEntitiesRoute;
    private readonly IHttpClientFactory transportFactory;
    private readonly PersonalDataAnalyzerProfile profile;
    private readonly IReadOnlyList<SensitiveContentRule> requestedRules;

    /// <summary>Initializes the probe for the categories a deployment switched on.</summary>
    /// <param name="plan">What this deployment scans for, of which the personal-data half is read.</param>
    /// <param name="profile">Where the analyzer is and what language it is asked in.</param>
    /// <param name="transportFactory">Opens the client the probe is made through.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the plan does not switch this scanner on.</exception>
    public PresidioAnalyzerProbe(
        SensitiveContentPlan plan,
        PersonalDataAnalyzerProfile profile,
        IHttpClientFactory transportFactory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transportFactory);

        this.requestedRules = [.. PresidioEntityCorpus.RequestedRules(plan).Values];
        this.profile = profile;
        this.transportFactory = transportFactory;

        // The language is already narrowed to two lowercase letters by the time a profile exists, so nothing here can
        // reach past the query argument it is written into.
        this.supportedEntitiesRoute = new Uri(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}?language={1}",
                SupportedEntitiesRoute,
                profile.Language),
            UriKind.Relative);
    }

    /// <inheritdoc />
    public async Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        var supported = await this.ReadSupportedEntitiesAsync(cancellationToken);

        // Per category rather than over the whole set, because a category is the unit an operator configures and the unit
        // a placeholder names. One entity of a category being absent is an analyzer with a narrower registry than the
        // shipped default and costs recall inside a category that still works; every entity of it being absent is a
        // category that would be scanned for and never found.
        var unrecognized = this.requestedRules
            .GroupBy(rule => rule.Category)
            .Where(category => !category.Any(rule => supported.Contains(rule.Name)))
            .Select(category => category.Key)
            .FirstOrDefault();

        if (unrecognized is not null)
        {
            throw PersonalDataAnalyzerUnavailableException.DetectsNothingFor(
                this.profile.Endpoint.ToString(),
                unrecognized);
        }
    }

    private async Task<HashSet<string>> ReadSupportedEntitiesAsync(CancellationToken cancellationToken)
    {
        string[]? supported;

        try
        {
            using var transport = this.transportFactory.CreateClient(PersonalDataAnalyzerProfile.TransportName);
            using var response = await transport.GetAsync(this.supportedEntitiesRoute, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // The status alone, as its number and .NET's own name for it. A refusal's body is written by a service this
                // process does not own, and so is the reason phrase beside the status line — a proxy or a wrong service at
                // the configured address composes both, and this message reaches a startup log.
                throw PersonalDataAnalyzerUnavailableException.Refused(
                    this.profile.Endpoint.ToString(),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:D} {1}",
                        response.StatusCode,
                        response.StatusCode));
            }

            supported = await response.Content.ReadFromJsonAsync(
                PresidioJsonContext.Default.StringArray,
                cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException
            and not PersonalDataAnalyzerUnavailableException)
        {
            throw PersonalDataAnalyzerUnavailableException.NotReached(this.profile.Endpoint.ToString(), failure);
        }

        if (supported is null or { Length: 0 })
        {
            throw PersonalDataAnalyzerUnavailableException.Refused(
                this.profile.Endpoint.ToString(),
                "an empty list of supported entities");
        }

        return [.. supported];
    }
}
