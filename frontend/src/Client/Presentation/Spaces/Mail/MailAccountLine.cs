// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Accounts;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Spaces.Mail;

/// <summary>One mailbox as the Mail space lists it: what it is called, whether it is being refreshed, and how current it is.</summary>
/// <param name="Id">The identifier the account was declared under, which is this row's identity across a refresh.</param>
/// <param name="DisplayName">The name the account is published under, which is what a person recognizes.</param>
/// <param name="Standing">Whether the deployment is refreshing this account, said in the words a person acts on.</param>
/// <param name="Freshness">How long ago the account last took mail in, said as a gap rather than as an instant.</param>
/// <param name="IsFailing">Whether the deployment's last attempt at this account did not complete.</param>
/// <remarks>
/// <para>
/// Two sentences rather than one, because they answer different halves of one question and a screen that merged them
/// would say the wrong thing about half its rows. The standing says whether the copy is still being refreshed; the
/// freshness says how old it is. An account failing since yesterday and an account nobody has written to since
/// yesterday carry the same gap and are not the same situation.
/// </para>
/// <para>
/// Nothing of the mailbox is on it. MailFathom's own name for the account is what a person recognizes their mailbox
/// by, and the address, the mail server, the folders, and every message stay where they are — this row is put in front
/// of somebody and reaches no log and no telemetry, so what it may carry is exactly what it does.
/// </para>
/// <para>
/// <c>Id</c> is the key MVUX matches rows by across a refresh, which is what keeps a list from rebuilding every row
/// each time the accounts are read again.
/// </para>
/// </remarks>
public sealed partial record MailAccountLine(
    string Id,
    string DisplayName,
    string Standing,
    string Freshness,
    bool IsFailing)
{
    /// <summary>The prefix under which each standing's sentence is authored.</summary>
    private const string StandingKeyPrefix = "MailPage.Account.Standing.";

    /// <summary>The prefix under which each gap's sentence is authored.</summary>
    private const string FreshnessKeyPrefix = "MailPage.Account.Freshness.";

    /// <summary>Describes one account as a row, in the language the application is being read in.</summary>
    /// <param name="account">What the deployment reported about the account.</param>
    /// <param name="now">When the gap is measured from.</param>
    /// <param name="words">Where the two sentences come from, since both are composed rather than fixed per control.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public static MailAccountLine Of(DeploymentMailAccount account, DateTimeOffset now, IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(words);

        var standing = account.Standing;

        return new MailAccountLine(
            account.Id,
            account.DisplayName,
            words[StandingResourceKeyFor(standing)].Value,
            words[FreshnessResourceKeyFor(GapAt(account.LastSynchronizedAt, now))].Value,
            standing is MailAccountStanding.Failing);
    }

    /// <summary>Says how long ago the account last took anything in, in the band a person decides on.</summary>
    /// <param name="lastSynchronizedAt">When the account last durably took mail in, or <see langword="null" /> where it never has.</param>
    /// <param name="now">When the gap is measured from.</param>
    /// <returns>The band the gap falls in.</returns>
    /// <remarks>
    /// A timestamp ahead of <paramref name="now" /> reads as the narrowest band rather than as a negative gap. The two
    /// clocks are a person's device and a deployment somewhere else, so a few seconds of disagreement between them is
    /// ordinary and is not something to put on a screen.
    /// </remarks>
    internal static FreshnessGap GapAt(DateTimeOffset? lastSynchronizedAt, DateTimeOffset now) =>
        lastSynchronizedAt is not { } taken
            ? FreshnessGap.Never
            : (now - taken) switch
            {
                { TotalHours: < 1 } => FreshnessGap.WithinTheHour,
                { TotalDays: < 1 } => FreshnessGap.Today,
                { TotalDays: < 7 } => FreshnessGap.WithinTheWeek,
                _ => FreshnessGap.LongerAgo,
            };

    /// <summary>Names the entry a standing's sentence is authored under.</summary>
    /// <param name="standing">The standing to name.</param>
    /// <returns>The resource key.</returns>
    /// <remarks>Composed from the value rather than named by a <c>x:Uid</c>, so a standing added with no sentence behind it is what the resource-table suite names rather than a key a reader meets on the screen.</remarks>
    internal static string StandingResourceKeyFor(MailAccountStanding standing) =>
        $"{StandingKeyPrefix}{standing}";

    /// <summary>Names the entry a gap's sentence is authored under.</summary>
    /// <param name="gap">The gap to name.</param>
    /// <returns>The resource key.</returns>
    internal static string FreshnessResourceKeyFor(FreshnessGap gap) => $"{FreshnessKeyPrefix}{gap}";
}
