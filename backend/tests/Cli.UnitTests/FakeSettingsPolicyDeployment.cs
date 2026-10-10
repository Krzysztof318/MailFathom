// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the settings-policy routes of one scope.</summary>
/// <remarks>
/// One scope per deployment, the deployment's own or one organization's, and every other policy path answered as an
/// organization the deployment does not hold. That is what makes the route a command reached part of every assertion
/// here rather than a second one: a command addressing the wrong scope meets an absence instead of a policy.
/// </remarks>
internal static class FakeSettingsPolicyDeployment
{
    /// <summary>Gets the version a stored policy here is first read at, which a write is composed over.</summary>
    internal const long PolicyVersion = 3;

    /// <summary>Builds a deployment whose one scope holds the policy stated.</summary>
    /// <param name="organization">The organization the policy belongs to, or <see langword="null" /> where it is the deployment's own.</param>
    /// <param name="documents">What each successive read of the policy answers with, the last one repeating.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>
    /// More than one document is how a writer committing between the read and the write is arranged: the buffer is
    /// opened over the first, and the reading that reports what moved meets the next, one version on.
    /// </remarks>
    internal static FakeHttpMessageHandler Holding(Guid? organization, params string[] documents) =>
        Answering(organization, PolicyVersion, WriteCommitted, documents);

    /// <summary>Builds a deployment whose own scope stores no policy, which it answers as an empty document at version zero.</summary>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler StoringNoPolicy() =>
        Answering(organization: null, version: 0, WriteCommitted, ["{}"]);

    /// <summary>Builds a deployment that refuses every write to the policy, with the code and the sentence it names.</summary>
    /// <param name="organization">The organization the policy belongs to, or <see langword="null" /> where it is the deployment's own.</param>
    /// <param name="code">The five-digit code the refusal carries.</param>
    /// <param name="message">The sentence the refusal carries.</param>
    /// <param name="documents">What each successive read of the policy answers with, the last one repeating.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler RefusingTheWrite(
        Guid? organization,
        int code,
        string message,
        params string[] documents) =>
        Answering(
            organization,
            PolicyVersion,
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"committed":false,"version":{{PolicyVersion + 1}},"code":{{code}},"messages":[{{JsonSerializer.Serialize(message)}}]}"""),
            documents);

    /// <summary>Builds a deployment that answers every write as stating what the policy in force already states.</summary>
    /// <param name="organization">The organization the policy belongs to, or <see langword="null" /> where it is the deployment's own.</param>
    /// <param name="message">The sentence the answer carries.</param>
    /// <param name="documents">What each successive read of the policy answers with, the last one repeating.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>The answer names no code, which is what tells it from a refusal: nothing was wrong with what was saved.</remarks>
    internal static FakeHttpMessageHandler FindingNothingToChange(
        Guid? organization,
        string message,
        params string[] documents) =>
        Answering(
            organization,
            PolicyVersion,
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"committed":false,"version":{{PolicyVersion}},"code":null,"messages":[{{JsonSerializer.Serialize(message)}}]}"""),
            documents);

    /// <summary>The answer every committed write reports, which moves the policy one version on.</summary>
    private static string WriteCommitted => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"committed":true,"version":{{PolicyVersion + 1}},"code":null,"messages":[]}""");

    private static FakeHttpMessageHandler Answering(
        Guid? organization,
        long version,
        string writeAnswer,
        string[] documents)
    {
        var reads = 0;

        // Advanced where the policy is read rather than per request, so a suite's second document is what the second
        // reading of the policy meets rather than whatever the command happened to ask for next.
        string NextPolicy()
        {
            var read = Math.Min(reads++, documents.Length - 1);

            return Policy(organization, version + read, documents[read]);
        }

        return new((request, _) =>
            Task.FromResult(Answer(request, organization, writeAnswer, NextPolicy)));
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        Guid? organization,
        string writeAnswer,
        Func<string> nextPolicy)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path == AdminEndpointRoutes.SettingsPolicyPath(organization))
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, nextPolicy())
                : FakeAdminEndpoint.Json(HttpStatusCode.OK, writeAnswer);
        }

        if (path.StartsWith($"{AdminEndpointRoutes.OrganizationsPath}/", StringComparison.Ordinal))
        {
            // The problem document the organization routes answer an organization the deployment does not hold with.
            return FakeAdminEndpoint.Json(
                HttpStatusCode.NotFound,
                """{"status":404,"detail":"This deployment holds no such organization."}""");
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static string Policy(Guid? organization, long version, string document) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"organizationId":{{JsonSerializer.Serialize(organization)}},"version":{{version}},"document":{{JsonSerializer.Serialize(document)}}}""");
}
