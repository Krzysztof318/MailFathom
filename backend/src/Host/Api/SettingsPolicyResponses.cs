// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Policies;
using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Host.Api;

/// <summary>The settings policy one scope holds, as an editing session opens it.</summary>
/// <param name="OrganizationId">The organization whose policy this is, or <see langword="null" /> for the deployment's own.</param>
/// <param name="Version">The version the policy was read at, which a save states; <c>0</c> where the scope stores none.</param>
/// <param name="Document">The policy as a JSON object, which states nothing where the scope stores none.</param>
/// <remarks>
/// The policy travels as a document rather than as a typed body for the reason a mail account's declaration does: it
/// follows the shape of the records it governs, so a setting either of those gains needs nothing added here. Nothing
/// in it is redacted, because a policy states nothing about a secret.
/// </remarks>
internal sealed record SettingsPolicyResponse(Guid? OrganizationId, long Version, string Document)
{
    /// <summary>Describes a policy reading.</summary>
    /// <param name="policy">The policy as the administration read it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy" /> is <see langword="null" />.</exception>
    internal static SettingsPolicyResponse For(SettingsPolicyDocument policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return new SettingsPolicyResponse(policy.OrganizationId, policy.Version, policy.Json);
    }
}

/// <summary>The whole settings policy an editing session saved.</summary>
/// <param name="Version">The version the buffer was opened over.</param>
/// <param name="Document">The policy as the administrator saved it.</param>
internal sealed record SettingsPolicySaveRequest(long Version, string? Document);

/// <summary>What one write to a settings policy did.</summary>
/// <param name="Committed">Whether the policy moved to a new version.</param>
/// <param name="Version">The version now in force, whether the write committed, was refused, or changed nothing.</param>
/// <param name="Code">The five-digit code naming why the write was refused, and <see langword="null" /> where nothing refused it.</param>
/// <param name="Messages">One sentence per reason the write was refused or changed nothing; empty on a commit.</param>
/// <remarks>
/// A refusal arrives as an outcome with a success status rather than as an error, for the reason a write to a user's
/// record does: each is something the administrator corrects and continues from, and each carries the version the
/// next attempt is composed over. A message names a property by its path and repeats no value the policy states
/// beyond a language, a zone, or a recording level somebody misspelled.
/// </remarks>
internal sealed record SettingsPolicyWriteResponse(
    bool Committed,
    long Version,
    int? Code,
    IReadOnlyList<string> Messages)
{
    /// <summary>Describes what a write did.</summary>
    /// <param name="outcome">The outcome the administration reported.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outcome" /> is <see langword="null" />.</exception>
    internal static SettingsPolicyWriteResponse For(SettingsPolicyWriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new SettingsPolicyWriteResponse(
            outcome.IsCommitted,
            outcome.Version,
            outcome.Refusal.IsSpecified ? outcome.Refusal.Value : null,
            outcome.Messages);
    }
}
