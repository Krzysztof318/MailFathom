// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Corpus;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.Generation.AiContent;
using MimeKit;

namespace MailFathom.SyntheticMail.Commands;

/// <summary>The root command: generate a batch of invented mail and deliver it to one mailbox, or write it out.</summary>
/// <remarks>
/// Generating is what the tool is invoked for, so it is the root rather than a subcommand of one, and what varies
/// between invocations is the recipient and the shape of the batch; the credential is not, and never will be.
/// <c>replay</c> hangs beneath it because it is the other half of <c>--export</c> — a corpus this command wrote is
/// delivered by that one — and because a run that generates nothing shares none of the options above.
/// </remarks>
internal static class DeliverBatchCommand
{
    private const int DefaultCount = 50;
    private const int DefaultSpanDays = 90;
    private const int DefaultAttachmentBytes = 64 * 1024;
    private const int DefaultIntervalMilliseconds = 250;

    /// <summary>How much of a batch carries fabricated sensitive material when the invocation says nothing.</summary>
    /// <remarks>
    /// Not zero, because a mailbox with nothing to find in it is one a scanner cannot be seen working on, and this
    /// tool exists so that nobody has to reach for their own mail to get material worth scanning. A fifth is enough
    /// that an ordinary batch carries several of every kind and low enough that the corpus still reads as mail rather
    /// than as a credential dump. A run that wants a clean corpus asks for one, and the line it prints says which it
    /// produced either way.
    /// </remarks>
    private const int DefaultSensitivePercentage = 20;

    /// <summary>Builds the command tree.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The root command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static RootCommand Create(SyntheticMailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Argument<string> recipientArgument = new("recipient")
        {
            Description = "The mailbox every generated message is delivered to. The only real address a run touches; every invented participant is under a reserved .test domain.",
        };

        Option<int> countOption = new("--count", "-n")
        {
            Description = $"How many messages to generate, 1..{BatchArguments.MaximumCount}.",
            DefaultValueFactory = _ => DefaultCount,
        };

        Option<int?> seedOption = new("--seed")
        {
            Description = "What the corpus is derived from. Chosen randomly and reported when absent, so a run can always be repeated.",
        };

        Option<int> daysOption = new("--days")
        {
            Description = $"How far back from --until the message dates reach, 1..{BatchArguments.MaximumSpanDays}.",
            DefaultValueFactory = _ => DefaultSpanDays,
        };

        Option<string?> untilOption = new("--until")
        {
            Description = $"The newest day a generated message is dated, as {BatchArguments.DateFormat}. Defaults to today and is reported either way.",
        };

        Option<int> attachmentBytesOption = new("--attachment-bytes")
        {
            Description = $"The ceiling on one attachment, 0..{BatchArguments.MaximumAttachmentCeiling}. Zero generates a corpus carrying none.",
            DefaultValueFactory = _ => DefaultAttachmentBytes,
        };

        Option<int> sensitivePercentageOption = new("--sensitive-percentage")
        {
            Description = $"How often a message carries a fabricated secret or personal identifier, 0..{BatchArguments.MaximumSensitivePercentage}. Zero generates a corpus carrying none.",
            DefaultValueFactory = _ => DefaultSensitivePercentage,
        };

        Option<int> intervalOption = new("--interval")
        {
            Description = $"Milliseconds between two submissions, 0..{BatchArguments.MaximumIntervalMilliseconds}, so a real server is not hit with a burst.",
            DefaultValueFactory = _ => DefaultIntervalMilliseconds,
        };

        Option<string?> configurationOption = new("--config")
        {
            Description = $"The sending account to read. Defaults to '{SendingAccountFile.FileName}' beside the built command.",
        };

        Option<bool> dryRunOption = new("--dry-run")
        {
            Description = "Generate and list the corpus on standard output without connecting to anything. With --ai the provider is still called, because the content is what the listing lists; only the submission is skipped.",
        };

        Option<bool> aiOption = new("--ai")
        {
            Description = "Generate the message content with the configured OpenAI provider instead of the seeded vocabulary. The sending account is still required, and the content is the one input the seed does not reproduce.",
        };

        Option<string?> languageOption = new("--language")
        {
            Description = "The languages AI-generated messages are written in, comma-separated, as in en or en,pl,de. Defaults to en. Requires --ai.",
        };

        Option<string?> topicOption = new("--topic")
        {
            Description = "The topics AI-generated messages are written about, comma-separated, as in business or invoices,technical-support,travel. Defaults to every supported topic. Requires --ai.",
        };

        Option<string?> aiConfigurationOption = new("--ai-config")
        {
            Description = $"The AI provider to read. Defaults to '{SyntheticAiProviderFile.FileName}' beside the built command.",
        };

        Option<bool> conversationOption = new("--conversation")
        {
            Description = "Generate exchanges between the recipient and invented correspondents instead of a flat corpus, delivering one turn at a time and building each reply from the identifier the recipient's server assigned. Needs the 'mailbox' block in the sending account file, because both halves of a thread have to reach the mailbox.",
        };

        Option<int?> concurrencyOption = new("--concurrency")
        {
            Description = $"How many provider answers the generation waits for at once, 1..{BatchArguments.MaximumConcurrency}. Defaults to {BatchArguments.DefaultConcurrency}, changes what the run costs in time and nothing about the corpus a seed produces, and requires --ai.",
        };

        Option<int?> deliveryTimeoutOption = new("--delivery-timeout")
        {
            Description = $"Seconds to wait for a submitted message to appear in the recipient's mailbox, 1..{BatchArguments.MaximumDeliveryTimeoutSeconds}. Defaults to {BatchArguments.DefaultDeliveryTimeoutSeconds}. Requires --conversation.",
        };

        Option<string?> exportOption = new("--export")
        {
            Description = $"Write the generated corpus to this archive instead of delivering it, so that 'replay' can deliver it into any mailbox afterwards. Requires --conversation, '--sensitive-percentage 0', and a recipient under {SyntheticVocabulary.ReservedTopLevelDomain}.",
        };

        RootCommand command = new("Generate invented mail and deliver a batch of it over SMTP to a development mailbox.")
        {
            recipientArgument,
            countOption,
            seedOption,
            daysOption,
            untilOption,
            attachmentBytesOption,
            sensitivePercentageOption,
            intervalOption,
            configurationOption,
            dryRunOption,
            aiOption,
            languageOption,
            topicOption,
            aiConfigurationOption,
            conversationOption,
            deliveryTimeoutOption,
            concurrencyOption,
            exportOption,
            ReplayCorpusCommand.Create(context),
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            BatchArguments.Parse(
                result.GetValue(recipientArgument) ?? string.Empty,
                result.GetValue(seedOption),
                result.GetValue(countOption),
                result.GetValue(untilOption),
                result.GetValue(daysOption),
                result.GetValue(attachmentBytesOption),
                result.GetValue(sensitivePercentageOption),
                result.GetValue(intervalOption),
                result.GetValue(configurationOption),
                result.GetValue(dryRunOption),
                result.GetValue(aiOption),
                result.GetValue(languageOption),
                result.GetValue(topicOption),
                result.GetValue(aiConfigurationOption),
                result.GetValue(conversationOption),
                result.GetValue(deliveryTimeoutOption),
                result.GetValue(concurrencyOption),
                result.GetValue(exportOption),
                context.Clock),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        SyntheticMailContext context,
        BatchArguments arguments,
        CancellationToken cancellationToken) =>
        arguments.Conversation
            ? await RunConversationsAsync(context, arguments, cancellationToken)
            : await RunFlatBatchAsync(context, arguments, cancellationToken);

    private static async Task<int> RunFlatBatchAsync(
        SyntheticMailContext context,
        BatchArguments arguments,
        CancellationToken cancellationToken)
    {
        // The account is read before anything is generated, so a run that cannot possibly deliver says so immediately
        // rather than after producing a corpus. A dry run is the one case here that needs no credential at all.
        var account = arguments.Submits ? context.ReadAccount(arguments.ConfigurationPath) : null;

        var corpus = arguments.AiContent
            ? await GenerateAiCorpusAsync(context, arguments, cancellationToken)
            : SyntheticEmailGenerator.Generate(arguments.ToPlan());

        ReportPlan(context, arguments);

        if (account is null)
        {
            ListCorpus(context, corpus);

            return SyntheticMailExitCode.Success;
        }

        return await DeliverAsync(context, arguments, account, corpus, cancellationToken);
    }

    /// <summary>Generates the batch as exchanges, and either delivers it, writes it, or lists it.</summary>
    /// <remarks>
    /// Both accounts are read before anything is generated: an exchange that could not read the mailbox back could not
    /// build a single reply, and finding that out after a provider has written two hundred messages costs a batch. A
    /// dry run and an export need no credential for either — the mailbox's own address is the recipient the invocation
    /// already named, which is all the generator needs to author half the turns.
    /// </remarks>
    private static async Task<int> RunConversationsAsync(
        SyntheticMailContext context,
        BatchArguments arguments,
        CancellationToken cancellationToken)
    {
        (SendingAccount Account, WatchedMailboxAccount Mailbox)? accounts = arguments.Submits
            ? ExchangeDelivery.ReadAccounts(context, arguments.ConfigurationPath, arguments.Recipient)
            : null;

        // The corpus file is opened before anything is generated, on the same terms as the accounts: a path that is
        // already taken, misspelled, or not writable would otherwise be found after a provider has written a hundred
        // messages, which is a paid batch lost to a typo.
        using var destination = arguments.ExportPath is { } reserved ? context.CreateCorpus(reserved) : null;

        var mailboxParticipant = ParticipantOf(arguments.Recipient);
        var conversations = arguments.AiContent
            ? await SyntheticEmailGenerator.GenerateConversationsAsync(
                arguments.ToPlan(),
                mailboxParticipant,
                OpenReportingContentSource(context, arguments),
                arguments.Concurrency,
                cancellationToken)
            : SyntheticEmailGenerator.GenerateConversations(arguments.ToPlan(), mailboxParticipant);

        ReportPlan(context, arguments);

        if (destination is not null && arguments.ExportPath is { } path)
        {
            ExportConversations(context, arguments, destination, path, conversations);

            return SyntheticMailExitCode.Success;
        }

        if (accounts is not { } delivering)
        {
            ListConversations(context, conversations);

            return SyntheticMailExitCode.Success;
        }

        var report = await ExchangeDelivery.DeliverAsync(
            context,
            delivering.Account,
            delivering.Mailbox,
            arguments.Recipient,
            [.. conversations.Select(DeliverableTurn.From)],
            arguments.Interval,
            arguments.DeliveryTimeout,
            cancellationToken);

        return ExchangeDelivery.Report(context.Console, arguments.Recipient, report);
    }

    /// <summary>Writes the generated exchanges as a corpus, which is what makes generation something paid for once.</summary>
    /// <remarks>
    /// The invocation recorded beside the messages is the repeat line the run already prints, so a second corpus is
    /// generated by copying that line and adding an export of its own — the seed reproduces the envelope, and in AI
    /// content mode a provider writes the words afresh.
    /// </remarks>
    private static void ExportConversations(
        SyntheticMailContext context,
        BatchArguments arguments,
        Stream destination,
        string path,
        IReadOnlyList<SyntheticConversation> conversations)
    {
        CorpusArchive.Write(destination, arguments.RepeatCommandLine, conversations, arguments.Recipient);

        context.Console.WriteError(string.Create(
            CultureInfo.InvariantCulture,
            $"Exported {conversations.Sum(conversation => conversation.Messages.Count)} messages in {conversations.Count} exchanges to '{path}'. Replay it with: replay {path} <recipient>"));
    }

    /// <summary>Reads the invented participant the watched mailbox writes as.</summary>
    /// <remarks>The address carries a display name only where the invocation wrote one, and a message signed by an address rather than by a person is what a bare address would produce.</remarks>
    private static SyntheticParticipant ParticipantOf(MailboxAddress address) => new(
        string.IsNullOrWhiteSpace(address.Name) ? address.Address : address.Name,
        address.Address);

    private static async Task<IReadOnlyList<SyntheticEmail>> GenerateAiCorpusAsync(
        SyntheticMailContext context,
        BatchArguments arguments,
        CancellationToken cancellationToken)
    {
        return await SyntheticEmailGenerator.GenerateAsync(
            arguments.ToPlan(),
            OpenReportingContentSource(context, arguments),
            arguments.Concurrency,
            cancellationToken);
    }

    /// <summary>Opens the source the run generates through, wrapped in the one that reports each answer as it lands.</summary>
    /// <remarks>
    /// The provider is read here, before anything is generated, for the reason the account is: a run that cannot
    /// possibly generate says so immediately rather than after the corpus has been half-answered. A dry run reads it
    /// too, because the content is what a dry run lists.
    /// </remarks>
    private static ReportingAiEmailContentSource OpenReportingContentSource(
        SyntheticMailContext context,
        BatchArguments arguments) =>
        new(
            context.OpenAiContentSource(context.ReadAiProvider(arguments.AiConfigurationPath!)),
            context.Console,
            arguments.Count);

    private static async Task<int> DeliverAsync(
        SyntheticMailContext context,
        BatchArguments arguments,
        SendingAccount account,
        IReadOnlyList<SyntheticEmail> corpus,
        CancellationToken cancellationToken)
    {
        context.Console.WriteError(string.Create(
            CultureInfo.InvariantCulture,
            $"Submitting as {account.Address.Address} to {account.Host}:{account.Port} over {account.Security}."));

        await using var transport = context.OpenTransport(account);

        await transport.OpenAsync(cancellationToken);

        var report = await new SyntheticMailBatchDelivery(transport, context.Console, context.Clock).DeliverAsync(
            corpus,
            account,
            arguments.Recipient,
            arguments.Interval,
            cancellationToken);

        return ExchangeDelivery.Report(context.Console, arguments.Recipient, report);
    }

    private static void ReportPlan(SyntheticMailContext context, BatchArguments arguments)
    {
        var conversation = arguments.Conversation
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" Delivered as exchanges with {arguments.Recipient.Address}, each reply built from the identifier its server assigned, waiting up to {arguments.DeliveryTimeout.TotalSeconds:0} seconds per delivery.")
            : string.Empty;

        var aiContent = arguments.AiContent
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" AI content in {string.Join(", ", arguments.Languages)} over {string.Join(", ", arguments.Topics.Select(topic => topic.Name))}, generated by the configured provider {arguments.Concurrency} answers at a time.")
            : string.Empty;

        context.Console.WriteError(string.Create(
            CultureInfo.InvariantCulture,
            $"Seed {arguments.Seed}: {arguments.Count} messages dated {arguments.EarliestDate:yyyy-MM-dd}..{arguments.LatestDate:yyyy-MM-dd}, attachments up to {arguments.MaximumAttachmentBytes} bytes, {arguments.SensitivePercentage}% carrying fabricated sensitive material.{conversation}{aiContent}"));

        context.Console.WriteError($"Repeat this batch with: {arguments.RepeatCommandLine}");
    }

    private static void ListCorpus(SyntheticMailContext context, IReadOnlyList<SyntheticEmail> corpus)
    {
        foreach (var email in corpus)
        {
            context.Console.WriteLine(CorpusListing.Describe(email));
        }
    }

    /// <summary>Lists the exchanges a run would deliver, one line per turn.</summary>
    /// <remarks>
    /// The ancestry every line carries is the one the seed produced rather than the one delivery would write, because
    /// the identifiers a reply is actually built from come from a server this run never reached. The exchange, its
    /// order, and its two sides are the seed's and are exactly what a dry run is read for.
    /// </remarks>
    private static void ListConversations(
        SyntheticMailContext context,
        IReadOnlyList<SyntheticConversation> conversations)
    {
        for (var thread = 0; thread < conversations.Count; thread++)
        {
            var conversation = conversations[thread];

            for (var turn = 0; turn < conversation.Messages.Count; turn++)
            {
                context.Console.WriteLine(CorpusListing.DescribeTurn(
                    conversation.Messages[turn],
                    thread,
                    turn,
                    SyntheticConversation.SideOf(turn)));
            }
        }
    }
}
