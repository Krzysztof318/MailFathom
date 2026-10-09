// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.DefaultAdministrator;

/// <summary>Where the record that a deployment's default administrator was written, and that its password setting was applied, is kept.</summary>
/// <remarks>
/// Both acts happen at most once per deployment, and several replicas start at once, so each is one statement against a
/// row every replica contends for rather than a read followed by a write: the replica that wins writes, and every other
/// one reads what it wrote.
/// </remarks>
public interface IDefaultAdministratorStore
{
    /// <summary>Records the default administrator unless this deployment ever recorded one, and reports the administrator it holds.</summary>
    /// <param name="candidate">The identifier the administrator is recorded under where this call is the one that records it.</param>
    /// <param name="assignmentId">The identifier of the assignment giving it the seeded administrator role at the deployment scope.</param>
    /// <param name="recordedAt">When the record is written.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the deployment holds afterwards: the administrator, or none where it was recorded once and removed since.</returns>
    /// <remarks>
    /// The user, its endpoint switches turned off, the assignment, and the record that all three were written commit
    /// together, so no start can leave a deployment holding the record without the user or the user without its grant.
    /// </remarks>
    Task<DefaultAdministratorRecord> RecordOnceAsync(
        UserId candidate,
        Guid assignmentId,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken);

    /// <summary>Gives the default administrator the password the deployment was started with, unless that setting was ever applied.</summary>
    /// <param name="administrator">The default administrator.</param>
    /// <param name="credentialId">The identifier the credential is provisioned under where this call writes it.</param>
    /// <param name="lookup">The username the credential is signed in with.</param>
    /// <param name="passwordHash">The stored record of the password.</param>
    /// <param name="appliedAt">When the setting is applied.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the call did, or why it did nothing.</returns>
    /// <remarks>
    /// The credential, presented on the administrative endpoint alone and narrowing nothing its user holds, and the record that the setting was applied commit
    /// together. A deployment whose administrator already holds a password credential records the setting as applied and
    /// writes nothing else, so a credential deleted later cannot bring the setting back on the next start.
    /// </remarks>
    Task<DefaultAdministratorPasswordOutcome> ApplyPasswordSettingAsync(
        UserId administrator,
        Guid credentialId,
        UserCredentialLookup lookup,
        string passwordHash,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken);
}
