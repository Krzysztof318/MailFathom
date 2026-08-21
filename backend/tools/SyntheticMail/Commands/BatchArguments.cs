// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.SyntheticMail.Commands;

/// <summary>One invocation, with every value checked and every default already resolved.</summary>
/// <param name="Recipient">The one real address the batch is delivered to.</param>
/// <param name="Seed">What the corpus is derived from, chosen randomly when the invocation named none.</param>
/// <param name="Count">How many messages to generate.</param>
/// <param name="LatestDate">The newest day a generated message is dated.</param>
/// <param name="SpanDays">How far back from <paramref name="LatestDate" /> the dates reach.</param>
/// <param name="MaximumAttachmentBytes">The ceiling on one attachment, and zero for a batch that carries none.</param>
/// <param name="SensitivePercentage">How often a message carries fabricated sensitive material, in messages per hundred.</param>
/// <param name="Interval">How long the run waits between two submissions.</param>
/// <param name="ConfigurationPath">Where the sending account is read from.</param>
/// <param name="DryRun">Whether to generate and report without submitting anything.</param>
/// <remarks>
/// Defaults are resolved here rather than left to the point of use, which is what makes
/// <see cref="RepeatCommandLine" /> possible: a run that chose its own seed and its own end date can print the exact
/// invocation that reproduces it, so repeating what somebody observed is copying a line rather than reconstructing it.
/// </remarks>
internal sealed record BatchArguments(
    MailboxAddress Recipient,
    int Seed,
    int Count,
    DateOnly LatestDate,
    int SpanDays,
    int MaximumAttachmentBytes,
    int SensitivePercentage,
    TimeSpan Interval,
    string ConfigurationPath,
    bool DryRun)
{
    /// <summary>The largest batch one invocation may ask for.</summary>
    /// <remarks>
    /// A ceiling rather than no limit, because the command submits to a real server: a mistyped count is otherwise a
    /// developer discovering they have sent a hundred thousand messages to an account somebody else also uses. Filling
    /// a mailbox further is a second invocation, which is a deliberate act.
    /// </remarks>
    internal const int MaximumCount = 2000;

    /// <summary>The largest attachment one message may carry.</summary>
    internal const int MaximumAttachmentCeiling = 10 * 1024 * 1024;

    /// <summary>The longest range the dates may be spread over, in days.</summary>
    internal const int MaximumSpanDays = 3650;

    /// <summary>The largest share of a batch that may carry fabricated sensitive material, in messages per hundred.</summary>
    /// <remarks>
    /// A hundred is a supported answer rather than a mistyped one: a corpus in which every message carries something
    /// to find is what a scanner is compared against a corpus in which none does, and both are one invocation.
    /// </remarks>
    internal const int MaximumSensitivePercentage = 100;

    /// <summary>The longest pause the run will take between two submissions, in milliseconds.</summary>
    internal const int MaximumIntervalMilliseconds = 60_000;

    /// <summary>The format a date is written and read in, which is the only one accepted.</summary>
    internal const string DateFormat = "yyyy-MM-dd";

    /// <summary>The newest instant a generated message is dated.</summary>
    /// <remarks>The end of the named day rather than its start, so the last day of the range holds mail like every other one.</remarks>
    internal DateTimeOffset LatestSentAt =>
        new(this.LatestDate.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);

    /// <summary>The oldest day a generated message is dated.</summary>
    internal DateOnly EarliestDate => this.LatestDate.AddDays(-this.SpanDays);

    /// <summary>The invocation that reproduces this run exactly.</summary>
    internal string RepeatCommandLine => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.Recipient.Address} --seed {this.Seed} --count {this.Count} --days {this.SpanDays} --until {this.LatestDate.ToString(DateFormat, CultureInfo.InvariantCulture)} --attachment-bytes {this.MaximumAttachmentBytes} --sensitive-percentage {this.SensitivePercentage}");

    /// <summary>Checks one invocation and resolves what it left unsaid.</summary>
    /// <param name="recipient">The address given as the argument.</param>
    /// <param name="seed">The seed, or <see langword="null" /> to choose one.</param>
    /// <param name="count">How many messages to generate.</param>
    /// <param name="until">The newest date, or <see langword="null" /> for today.</param>
    /// <param name="days">How far back the dates reach.</param>
    /// <param name="attachmentBytes">The ceiling on one attachment.</param>
    /// <param name="sensitivePercentage">How often a message carries fabricated sensitive material.</param>
    /// <param name="intervalMilliseconds">How long to wait between two submissions.</param>
    /// <param name="configurationPath">Where to read the sending account, or <see langword="null" /> for the default.</param>
    /// <param name="dryRun">Whether to submit anything at all.</param>
    /// <param name="timeProvider">What today is read from.</param>
    /// <returns>The checked invocation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recipient" /> or <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when a value is missing or outside its bounds, with a message naming the option.</exception>
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "The drawn value is a corpus label the run then prints so it can be repeated. It authenticates nothing, is published by design, and being unguessable would defeat its purpose.")]
    internal static BatchArguments Parse(
        string recipient,
        int? seed,
        int count,
        string? until,
        int days,
        int attachmentBytes,
        int sensitivePercentage,
        int intervalMilliseconds,
        string? configurationPath,
        bool dryRun,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var latestDate = ParseLatestDate(until, timeProvider);
        var spanDays = Bounded(days, 1, MaximumSpanDays, "--days");

        // A range reaching past the first representable day would throw out of DateOnly rather than out of this method,
        // which is a stack trace where a mistyped `--until` deserves a sentence.
        if (latestDate.DayNumber < spanDays)
        {
            throw new SyntheticMailFailure(
                $"'--until {latestDate.ToString(DateFormat, CultureInfo.InvariantCulture)}' with '--days {spanDays}' reaches before the first representable date.");
        }

        return new BatchArguments(
            ParseRecipient(recipient),
            // Random.Shared rather than a seed of its own, because this draw decides nothing a run has to reproduce —
            // it is the value the run then reports so that the *next* run can.
            seed ?? Random.Shared.Next(),
            Bounded(count, 1, MaximumCount, "--count"),
            latestDate,
            spanDays,
            Bounded(attachmentBytes, 0, MaximumAttachmentCeiling, "--attachment-bytes"),
            Bounded(sensitivePercentage, 0, MaximumSensitivePercentage, "--sensitive-percentage"),
            TimeSpan.FromMilliseconds(Bounded(intervalMilliseconds, 0, MaximumIntervalMilliseconds, "--interval")),
            string.IsNullOrWhiteSpace(configurationPath) ? SendingAccountFile.DefaultPath() : configurationPath,
            dryRun);
    }

    /// <summary>Builds the plan this invocation describes.</summary>
    /// <returns>The plan, which is everything the generator reads.</returns>
    internal SyntheticCorpusPlan ToPlan() =>
        new(
            this.Seed,
            this.Count,
            this.LatestSentAt,
            this.SpanDays,
            this.MaximumAttachmentBytes,
            this.SensitivePercentage);

    private static MailboxAddress ParseRecipient(string recipient) =>
        MailboxAddress.TryParse(recipient, out var parsed)
            ? parsed
            : throw new SyntheticMailFailure($"'{recipient}' is not a mail address.");

    private static DateOnly ParseLatestDate(string? until, TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(until))
        {
            return DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        }

        return DateOnly.TryParseExact(until, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new SyntheticMailFailure($"'--until {until}' is not a date written as {DateFormat}.");
    }

    private static int Bounded(int value, int lowest, int highest, string option) =>
        value < lowest || value > highest
            ? throw new SyntheticMailFailure(
                string.Create(CultureInfo.InvariantCulture, $"'{option} {value}' is outside {lowest}..{highest}."))
            : value;
}
