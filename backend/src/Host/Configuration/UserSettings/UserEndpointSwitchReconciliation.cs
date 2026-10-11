// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users.EndpointSwitches;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Derives again, from the effective record, the endpoint switches of every user whose row was written past the settings policy.</summary>
/// <remarks>
/// <para>
/// During a rolling upgrade a build that does not know settings policies still writes a user's record and still
/// moves a user between organizations, and it puts the record's own switches on the row — over whatever a policy
/// forces or supplies. It leaves the row's stamp behind when it does, so such a row is found here, on every
/// replica's convergence interval, and its switches are derived from the record as the policies governing it
/// resolve it, exactly as every other writer of them derives them.
/// </para>
/// <para>
/// <see cref="MaximumUsersPerReading" /> rows are read per interval, in identifier order, and each reading starts
/// past the user the one before it ended on. A reading that passes the last trailing row with room left turns back
/// to the first within the same interval and reads what is left of that many, so every trailing row is reached on
/// each interval while no more than that many trail, wherever the last reading ended. A larger backlog drains that
/// many per interval, and a row that cannot be stamped — a record governed by a policy this build does not read,
/// above all — is passed rather than read first again on every interval, where it would keep every row behind it
/// waiting for as long as it stays that way. Where a reading ended is this replica's own and nothing rests on it:
/// a replica that starts reads from the first row, and what it repeats is derived to the same switches.
/// </para>
/// <para>
/// Every replica runs it, and two that reach one row are told apart by the row's lock: the one that waited finds the
/// stamp matching and writes nothing.
/// </para>
/// <para>
/// One user whose derivation fails is reported and stepped over rather than ending the reading, for the reason
/// <see cref="MailAccountSettingsReconciliation" /> gives.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this service.")]
internal sealed partial class UserEndpointSwitchReconciliation(
    IUserEndpointSwitchStore switches,
    IUserRecordPolicyJudge judge,
    ILogger<UserEndpointSwitchReconciliation> logger)
{
    /// <summary>The most users whose switches are derived again per interval, so a long backlog is caught up over several rather than in one unbounded read.</summary>
    internal const int MaximumUsersPerReading = 100;

    private Guid? readingEndedOn;

    /// <summary>Derives again the endpoint switches of up to <see cref="MaximumUsersPerReading" /> users whose stamp does not match their row.</summary>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>A task that completes once each of those users was derived, left as they stand, or reported.</returns>
    /// <remarks>Called by one worker in turn and never beside itself, which is what lets where a reading ended be a field.</remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One user whose switches cannot be derived is reported and stepped over, so the users behind them in identifier order are not held back by it on every interval.")]
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        foreach (var user in await this.ReadThisIntervalsUsersAsync(cancellationToken))
        {
            try
            {
                await switches.DeriveAgainAsync(user, this.DeriveFromEffectiveRecord, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure)
            {
                this.LogUserNotDerived(user.Value, failure);
            }
        }
    }

    /// <summary>Reads on from where the last interval's reading ended, and turns back to the first row where that leaves room.</summary>
    /// <returns>No more than <see cref="MaximumUsersPerReading" /> users, each once.</returns>
    /// <remarks>
    /// Turning back inside the interval, rather than on the next one, is what keeps the documented bound: a row an
    /// older build writes behind where the last reading ended would otherwise wait for a reading that came back
    /// short and then for the one after it. The rows read from the first can repeat ones already read past the
    /// position — nothing was derived between the two readings — so they are told apart here.
    /// </remarks>
    private async Task<IReadOnlyList<UserId>> ReadThisIntervalsUsersAsync(CancellationToken cancellationToken)
    {
        var startedPast = this.readingEndedOn;
        var trailing = await switches.ReadTrailingAsync(startedPast, MaximumUsersPerReading, cancellationToken);

        if (startedPast is null || trailing.Count == MaximumUsersPerReading)
        {
            this.readingEndedOn = trailing.Count == MaximumUsersPerReading ? trailing[^1].Value : null;

            return trailing;
        }

        var room = MaximumUsersPerReading - trailing.Count;
        var fromTheFirst = await switches.ReadTrailingAsync(after: null, room, cancellationToken);

        this.readingEndedOn = fromTheFirst.Count == room ? fromTheFirst[^1].Value : null;

        return [.. trailing, .. fromTheFirst.Except(trailing)];
    }

    /// <summary>Derives the switches a row is to carry by resolving the record through the policies governing it.</summary>
    /// <returns>The switches, or <see langword="null" /> where this build can resolve neither the record nor a force over it, so the row is left as it stands and read again later.</returns>
    private UserEndpointAccess? DeriveFromEffectiveRecord(GovernedUserRecord record) => judge.Judge([record])[0].EndpointAccess;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The endpoint switches of user {UserId} could not be derived again from their record and the settings policies governing it, so requests go on being admitted for them by what their row held; they are derived again on a later interval.")]
    private partial void LogUserNotDerived(Guid userId, Exception exception);
}
