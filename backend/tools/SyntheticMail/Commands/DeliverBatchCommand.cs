// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation;

namespace MailFathom.SyntheticMail.Commands;

/// <summary>The whole command: generate a batch of invented mail and deliver it to one mailbox.</summary>
/// <remarks>
/// A root command with no subcommands, because the tool does one thing. What varies between invocations is the
/// recipient and the shape of the batch, and both are arguments; the credential is not, and never will be.
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
            Description = "Generate and list the corpus on standard output without connecting to anything.",
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
                context.Clock),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        SyntheticMailContext context,
        BatchArguments arguments,
        CancellationToken cancellationToken)
    {
        // The account is read before anything is generated, so a run that cannot possibly deliver says so immediately
        // rather than after producing a corpus. A dry run is the one case that needs no credential at all.
        var account = arguments.DryRun ? null : context.ReadAccount(arguments.ConfigurationPath);
        var corpus = SyntheticEmailGenerator.Generate(arguments.ToPlan());

        ReportPlan(context, arguments);

        if (account is null)
        {
            ListCorpus(context, corpus);

            return SyntheticMailExitCode.Success;
        }

        return await DeliverAsync(context, arguments, account, corpus, cancellationToken);
    }

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

        var report = await new SyntheticMailBatchDelivery(transport, context.Clock).DeliverAsync(
            corpus,
            account,
            arguments.Recipient,
            arguments.Interval,
            cancellationToken);

        return ReportDelivery(context, arguments, report);
    }

    private static void ReportPlan(SyntheticMailContext context, BatchArguments arguments)
    {
        context.Console.WriteError(string.Create(
            CultureInfo.InvariantCulture,
            $"Seed {arguments.Seed}: {arguments.Count} messages dated {arguments.EarliestDate:yyyy-MM-dd}..{arguments.LatestDate:yyyy-MM-dd}, attachments up to {arguments.MaximumAttachmentBytes} bytes, {arguments.SensitivePercentage}% carrying fabricated sensitive material."));

        context.Console.WriteError($"Repeat this batch with: {arguments.RepeatCommandLine}");
    }

    private static void ListCorpus(SyntheticMailContext context, IReadOnlyList<SyntheticEmail> corpus)
    {
        foreach (var email in corpus)
        {
            context.Console.WriteLine(CorpusListing.Describe(email));
        }
    }

    private static int ReportDelivery(
        SyntheticMailContext context,
        BatchArguments arguments,
        DeliveryReport report)
    {
        context.Console.WriteError(string.Create(
            CultureInfo.InvariantCulture,
            $"Delivered {report.Delivered} of {report.Attempted} to {arguments.Recipient.Address}."));

        if (report.Failures.Count == 0)
        {
            return SyntheticMailExitCode.Success;
        }

        foreach (var failure in report.Failures)
        {
            context.Console.WriteError($"  refused <{failure.MessageId}> \"{failure.Subject}\": {failure.Reason}");
        }

        return SyntheticMailExitCode.Failure;
    }
}
