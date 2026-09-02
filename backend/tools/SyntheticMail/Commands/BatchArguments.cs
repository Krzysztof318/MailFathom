// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// <param name="AiContent">Whether the message content comes from the configured provider rather than the seeded vocabulary.</param>
/// <param name="Languages">The languages AI-generated messages are written in, and empty when <see cref="AiContent" /> is <see langword="false" />.</param>
/// <param name="Topics">The topics AI-generated messages are written about, and empty when <see cref="AiContent" /> is <see langword="false" />.</param>
/// <param name="AiConfigurationPath">Where the AI provider is read from, or <see langword="null" /> when the run generates without one.</param>
/// <param name="Conversation">Whether the batch is generated and delivered as exchanges between two mailboxes rather than as a flat corpus.</param>
/// <param name="DeliveryTimeout">How long an exchange waits for a submitted message to appear in the watched mailbox.</param>
/// <param name="Concurrency">How many provider answers the generation waits for at once, and one when the run generates without a provider.</param>
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
    bool DryRun,
    bool AiContent,
    IReadOnlyList<string> Languages,
    IReadOnlyList<SyntheticMailTopic> Topics,
    string? AiConfigurationPath,
    bool Conversation,
    TimeSpan DeliveryTimeout,
    int Concurrency)
{
    /// <summary>The fewest messages an exchange can be generated from.</summary>
    /// <remarks>Two, because a thread of one message has nothing in it that a flat corpus does not already produce.</remarks>
    internal const int FewestConversationMessages = 2;

    /// <summary>The longest an exchange will wait for one submitted message to appear in the watched mailbox, in seconds.</summary>
    /// <remarks>
    /// Ten minutes, which is past anything but a greylisting relay holding the first message from an unknown sender.
    /// A bound rather than none, because a run that waits forever for a message a server silently dropped is one a
    /// developer has to notice and interrupt.
    /// </remarks>
    internal const int MaximumDeliveryTimeoutSeconds = 600;

    /// <summary>How long an exchange waits for one delivered copy when the invocation says nothing, in seconds.</summary>
    /// <remarks>Two minutes, which covers an ordinary relay's queue and is short enough that a mistyped mailbox is noticed on the first turn rather than after a batch.</remarks>
    internal const int DefaultDeliveryTimeoutSeconds = 120;

    /// <summary>How many provider answers a run waits for at once when the invocation says nothing.</summary>
    /// <remarks>
    /// Four, which turns an hour of waiting into a quarter of one and is below what any provider's ordinary key is
    /// rate-limited at. Higher is a number a developer picks against their own key rather than one this tool can know:
    /// what a provider answers a burst with is a refusal that says nothing about the mail.
    /// </remarks>
    internal const int DefaultConcurrency = 4;

    /// <summary>The most provider answers a run may wait for at once.</summary>
    /// <remarks>
    /// A ceiling rather than none, for the reason the batch size has one: a mistyped number is otherwise a run opening
    /// as many connections to a provider as the corpus has messages, which is answered with rate limiting rather than
    /// with a corpus.
    /// </remarks>
    internal const int MaximumConcurrency = 32;

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
    /// <remarks>
    /// In AI content mode the line reproduces the envelope — the seed, the distribution, the shape — and not the
    /// words, which are the provider's to answer and differ from run to run.
    /// </remarks>
    internal string RepeatCommandLine => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.Recipient.Address} --seed {this.Seed} --count {this.Count} --days {this.SpanDays} --until {this.LatestDate.ToString(DateFormat, CultureInfo.InvariantCulture)} --attachment-bytes {this.MaximumAttachmentBytes} --sensitive-percentage {this.SensitivePercentage}{this.ConversationCommandLine}{this.AiCommandLine}");

    /// <summary>The part of the repeat line an exchange adds, and nothing when the run produced a flat corpus.</summary>
    /// <remarks>
    /// The delivery bound is part of it because it decides which messages a run gave up on, so a batch repeated with a
    /// different one is not the batch that was observed — the seed reproduces the content, and this reproduces the
    /// terms it was delivered under.
    /// </remarks>
    private string ConversationCommandLine => this.Conversation
        ? string.Create(
            CultureInfo.InvariantCulture,
            $" --conversation --delivery-timeout {this.DeliveryTimeout.TotalSeconds:0}")
        : string.Empty;

    private string AiCommandLine => this.AiContent
        ? string.Create(
            CultureInfo.InvariantCulture,
            $" --ai --language {string.Join(",", this.Languages)} --topic {string.Join(",", this.Topics.Select(topic => topic.Name))}")
        : string.Empty;

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
    /// <param name="ai">Whether the message content comes from the configured provider.</param>
    /// <param name="language">The languages the content is written in, comma-separated, or <see langword="null" /> for the default.</param>
    /// <param name="topic">The topics the content is written about, comma-separated, or <see langword="null" /> for the default.</param>
    /// <param name="aiConfigurationPath">Where to read the AI provider, or <see langword="null" /> for the default.</param>
    /// <param name="conversation">Whether to generate and deliver exchanges rather than a flat corpus.</param>
    /// <param name="deliveryTimeoutSeconds">How long to wait for a submitted message to appear in the watched mailbox.</param>
    /// <param name="concurrency">How many provider answers the generation waits for at once, or <see langword="null" /> for the default.</param>
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
        bool ai,
        string? language,
        string? topic,
        string? aiConfigurationPath,
        bool conversation,
        int? deliveryTimeoutSeconds,
        int? concurrency,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var latestDate = ParseLatestDate(until, timeProvider);
        var spanDays = Bounded(days, 1, MaximumSpanDays, "--days");
        var messageCount = Bounded(count, 1, MaximumCount, "--count");
        var deliveryTimeout = ParseDeliveryTimeout(conversation, deliveryTimeoutSeconds);

        if (conversation && messageCount < FewestConversationMessages)
        {
            throw new SyntheticMailFailure(
                $"'--conversation' generates threads, and a thread holds at least {FewestConversationMessages} messages: raise '--count {messageCount}'.");
        }

        // A range reaching past the first representable day would throw out of DateOnly rather than out of this method,
        // which is a stack trace where a mistyped `--until` deserves a sentence.
        if (latestDate.DayNumber < spanDays)
        {
            throw new SyntheticMailFailure(
                $"'--until {latestDate.ToString(DateFormat, CultureInfo.InvariantCulture)}' with '--days {spanDays}' reaches before the first representable date.");
        }

        var (aiContent, languages, topics, resolvedAiConfigurationPath) = ParseAiMode(ai, language, topic, aiConfigurationPath);

        return new BatchArguments(
            ParseRecipient(recipient),
            // Random.Shared rather than a seed of its own, because this draw decides nothing a run has to reproduce —
            // it is the value the run then reports so that the *next* run can.
            seed ?? Random.Shared.Next(),
            messageCount,
            latestDate,
            spanDays,
            Bounded(attachmentBytes, 0, MaximumAttachmentCeiling, "--attachment-bytes"),
            Bounded(sensitivePercentage, 0, MaximumSensitivePercentage, "--sensitive-percentage"),
            TimeSpan.FromMilliseconds(Bounded(intervalMilliseconds, 0, MaximumIntervalMilliseconds, "--interval")),
            string.IsNullOrWhiteSpace(configurationPath) ? SendingAccountFile.DefaultPath() : configurationPath,
            dryRun,
            aiContent,
            languages,
            topics,
            resolvedAiConfigurationPath,
            conversation,
            deliveryTimeout,
            ParseConcurrency(aiContent, concurrency));
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
            this.SensitivePercentage,
            this.Languages,
            this.Topics);

    /// <summary>Resolves how many answers the generation waits for at once, and refuses the option outside the mode that reads it.</summary>
    /// <remarks>
    /// Refused outside <c>--ai</c> for the reason <c>--delivery-timeout</c> is refused outside <c>--conversation</c>:
    /// a corpus the vocabulary writes is produced in the time it takes to draw it, so there is nothing to overlap and
    /// a number written on one decides nothing. It is absent from the repeat line deliberately — it changes what a run
    /// costs in time and nothing about what it produces, so the line that reproduces a corpus does not carry it.
    /// </remarks>
    private static int ParseConcurrency(bool aiContent, int? concurrency)
    {
        if (!aiContent)
        {
            return concurrency is null
                ? 1
                : throw new SyntheticMailFailure(
                    "'--concurrency' spreads the provider calls a run makes, which only '--ai' makes.");
        }

        return Bounded(concurrency ?? DefaultConcurrency, 1, MaximumConcurrency, "--concurrency");
    }

    /// <summary>Resolves how long an exchange waits for a delivery, and refuses the option outside the mode that reads it.</summary>
    /// <remarks>
    /// Refused rather than ignored, for the reason naming a language without <c>--ai</c> is: a flat batch never looks
    /// for what it delivered, so a bound written on one decides nothing and a run that accepted it would report a
    /// repeat line naming a value that had no effect.
    /// </remarks>
    private static TimeSpan ParseDeliveryTimeout(bool conversation, int? deliveryTimeoutSeconds)
    {
        if (!conversation)
        {
            return deliveryTimeoutSeconds is null
                ? TimeSpan.Zero
                : throw new SyntheticMailFailure(
                    "'--delivery-timeout' bounds the wait for a delivered copy, which only '--conversation' waits for.");
        }

        return TimeSpan.FromSeconds(
            Bounded(
                deliveryTimeoutSeconds ?? DefaultDeliveryTimeoutSeconds,
                1,
                MaximumDeliveryTimeoutSeconds,
                "--delivery-timeout"));
    }

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

    /// <summary>Resolves the AI content mode: what was named, what was defaulted, and what is refused.</summary>
    /// <remarks>
    /// A mode without content options is a mode with defaults rather than an error: a run that asks for AI content
    /// without naming a language or a topic wants one written in English about whatever the tool ships, and a refusal
    /// there would make the default a second invocation to type. Naming content options without the mode is an error,
    /// because they decide nothing the seeded vocabulary reads.
    /// </remarks>
    private static (bool AiContent, IReadOnlyList<string> Languages, IReadOnlyList<SyntheticMailTopic> Topics, string? ConfigurationPath) ParseAiMode(
        bool ai,
        string? language,
        string? topic,
        string? aiConfigurationPath)
    {
        if (!ai)
        {
            if (!string.IsNullOrWhiteSpace(language) || !string.IsNullOrWhiteSpace(topic))
            {
                throw new SyntheticMailFailure("'--language' and '--topic' decide AI-generated content, which requires '--ai'.");
            }

            return (false, [], [], null);
        }

        return (
            true,
            ParseLanguages(language),
            ParseTopics(topic),
            string.IsNullOrWhiteSpace(aiConfigurationPath) ? SyntheticAiProviderFile.DefaultPath() : aiConfigurationPath);
    }

    /// <summary>Whether a value is written the way a language code is, which is the only shape accepted.</summary>
    /// <remarks>
    /// Two or three letters, for the reason ISO 639-1 and -3 are the two a model understands: the code is handed to
    /// the provider as the name of a language, and a code neither of those registers would be a name it does not know.
    /// The value is already lowercased where this is asked, so the check is letters and length rather than a pattern.
    /// </remarks>
    private static bool IsLanguageCode(string code) =>
        code.Length is 2 or 3 && code.All(char.IsAsciiLetter);

    private static List<string> ParseLanguages(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return ["en"];
        }

        var codes = new List<string>();

        foreach (var raw in language.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = raw.ToLowerInvariant();

            if (!IsLanguageCode(code))
            {
                throw new SyntheticMailFailure(
                    $"'--language {raw}' is not a language code: write two or three letters, comma-separated, as in en,pl,de.");
            }

            // Deduplicated in order of first naming, so a code written twice is one language, not two shares of one.
            if (!codes.Contains(code))
            {
                codes.Add(code);
            }
        }

        return codes.Count > 0
            ? codes
            : throw new SyntheticMailFailure(
                $"'--language {language}' names no language code: write two or three letters, comma-separated, as in en,pl,de.");
    }

    private static List<SyntheticMailTopic> ParseTopics(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return [.. SyntheticMailTopic.All];
        }

        var topics = new List<SyntheticMailTopic>();

        foreach (var raw in topic.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!SyntheticMailTopic.TryParse(raw, out var parsed))
            {
                throw new SyntheticMailFailure(
                    $"'{raw}' is not a topic: one of {string.Join(", ", SyntheticMailTopic.All.Select(candidate => candidate.Name))}.");
            }

            if (!topics.Contains(parsed))
            {
                topics.Add(parsed);
            }
        }

        return topics;
    }

    private static int Bounded(int value, int lowest, int highest, string option) =>
        value < lowest || value > highest
            ? throw new SyntheticMailFailure(
                string.Create(CultureInfo.InvariantCulture, $"'{option} {value}' is outside {lowest}..{highest}."))
            : value;
}
