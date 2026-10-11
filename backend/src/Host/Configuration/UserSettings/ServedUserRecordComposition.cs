// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.Records;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Binds one user's stored record and attributes whatever it refuses to the one declaration that introduced it.</summary>
/// <remarks>
/// <para>
/// One invalid declaration used to cost a user every mailbox they have, because the record a user is served from is
/// composed out of their document and every mail account assigned to them, and the binder judges that composition as one
/// document. So a single account whose row an older build wrote, a manual edit broke, or a newer rule tightened refused
/// the whole composition, and the user was served nothing.
/// </para>
/// <para>
/// The narrowing below is what keeps the cost where the defect is. The whole composition is bound first and is the only
/// thing that runs while everything is in order — a deployment whose records are sound pays exactly one binding per
/// user, as it always did. Only a composition that refuses is taken apart: the document is bound with no account at
/// all, which says whether the defect is in the user's own record or in a declaration beside it, and the accounts are
/// then added back one at a time in the order they were recorded. An account whose addition refuses is the account that
/// introduced the refusal — which is what makes a conflict between two declarations reject the second rather than both,
/// since the first is already in the set that bound.
/// </para>
/// <para>
/// A user's own record failing is the one case that costs the whole user, and it has to: everything a user is served
/// with comes out of that document, so a document that is not a record leaves nothing to serve them from. The caller
/// decides what to do about it — a start leaves that user unserved, and a convergence leaves them on the version they
/// last bound.
/// </para>
/// <para>
/// What is bound is the record as the settings policies governing the user resolve it rather than the document as
/// its row holds it, so a forced value and a default reach everything that is served from the answer. A policy this
/// build does not read as one costs the whole user for the same reason their own document does, and is answered the
/// same way: a record is never served on part of what governs it, so it is held back and the caller keeps whatever
/// it last composed.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this composition.")]
internal sealed class ServedUserRecordComposition(UserAccountDocumentBinder binder)
{
    /// <summary>Binds the record a user is served from, and says what it left behind.</summary>
    /// <param name="document">The user's row: their own document, the accounts assigned to them, and the version both were read at.</param>
    /// <param name="arrival">Whether the document is being written or is one the deployment already holds.</param>
    /// <returns>The bound record together with every declaration refused, or no record at all when the user's own document is not one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public ServedUserRecord Compose(UserSettingsDocument document, UserRecordArrival arrival)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (SettingsPolicyLayers.Read(document.Policies, SettingsPolicySection.Users, out var unreadable) is not { } layers)
        {
            return new ServedUserRecord(
                Record: null,
                [
                    new HeldBackRecord(
                        HeldBackRecordKind.User,
                        document.User.Value,
                        document.DisplayName,
                        document.Version,
                        [SettingsPolicyLayers.DescribeUnreadable(unreadable)]),
                ]);
        }

        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, document.Json).EffectiveJson;
        var declared = document.MailAccounts.Where(MailAccountRecordComposition.IsServed).ToArray();
        var whole = binder.Bind(MailAccountRecordComposition.Compose(resolved, declared), arrival);

        if (whole.User is { } bound)
        {
            return new ServedUserRecord(bound, []);
        }

        // A record declaring no account has nothing to narrow, so the one binding already made is the whole answer.
        var withoutAccounts = declared.Length == 0
            ? whole
            : binder.Bind(MailAccountRecordComposition.Compose(resolved, []), arrival);

        if (withoutAccounts.User is null)
        {
            // The refusals of the document on its own rather than of the whole composition, so a record that is both
            // unreadable and carries a broken declaration is reported by what is wrong with the record itself.
            return new ServedUserRecord(
                Record: null,
                [
                    new HeldBackRecord(
                        HeldBackRecordKind.User,
                        document.User.Value,
                        document.DisplayName,
                        document.Version,
                        withoutAccounts.Refusals),
                ]);
        }

        return this.ComposeFromTheAccountsThatBind(resolved, arrival, declared, withoutAccounts.User);
    }

    /// <summary>Adds the declared accounts back one at a time, keeping those that still bind beside the ones before them.</summary>
    private ServedUserRecord ComposeFromTheAccountsThatBind(
        string resolved,
        UserRecordArrival arrival,
        MailAccountRecord[] declared,
        UserAccountOptions withoutAccounts)
    {
        var kept = new List<MailAccountRecord>(declared.Length);
        var heldBack = new List<HeldBackRecord>();
        var bound = withoutAccounts;

        foreach (var account in declared)
        {
            var candidate = binder.Bind(
                MailAccountRecordComposition.Compose(resolved, [.. kept, account]),
                arrival);

            if (candidate.User is { } withAccount)
            {
                kept.Add(account);
                bound = withAccount;

                continue;
            }

            heldBack.Add(new HeldBackRecord(
                HeldBackRecordKind.MailAccount,
                account.Id,
                account.DisplayName,
                account.Version,
                candidate.Refusals));
        }

        return new ServedUserRecord(bound, heldBack);
    }
}
