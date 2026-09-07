// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Corpus;

namespace MailFathom.SyntheticMail.Commands;

/// <summary>Delivers an exported corpus into a mailbox, generating nothing.</summary>
/// <remarks>
/// <para>
/// The other half of <c>--export</c>, and the reason generation is something paid for once: a corpus whose content a
/// model wrote costs one generation, and every run after that reads what it produced. Nothing here reaches a provider,
/// so nothing here has an opinion about seeds, languages, topics, counts, or dates — those were decided when the
/// corpus was generated and are recorded in it.
/// </para>
/// <para>
/// What a replay does decide is where the mail goes and how fast: the mailbox, the account it submits as, the pacing
/// between submissions, and how long it waits for a delivered copy. Two replays of one corpus into two fresh mailboxes
/// fill them identically, because the messages are read rather than drawn — which is what makes a difference between
/// two runs of a pipeline a difference in the code.
/// </para>
/// </remarks>
internal static class ReplayCorpusCommand
{
    private const int DefaultIntervalMilliseconds = 250;

    /// <summary>Builds the subcommand.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The subcommand.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(SyntheticMailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Argument<string> corpusArgument = new("corpus")
        {
            Description = "The exported corpus to deliver, as written by --export.",
        };

        Argument<string> recipientArgument = new("recipient")
        {
            Description = "The mailbox the corpus is delivered into, which is the address the 'mailbox' block in the sending account file names.",
        };

        Option<string?> configurationOption = new("--config")
        {
            Description = $"The sending account to read. Defaults to '{SendingAccountFile.FileName}' beside the built command.",
        };

        Option<int> intervalOption = new("--interval")
        {
            Description = $"Milliseconds between two submissions, 0..{BatchArguments.MaximumIntervalMilliseconds}, so a real server is not hit with a burst.",
            DefaultValueFactory = _ => DefaultIntervalMilliseconds,
        };

        Option<int> deliveryTimeoutOption = new("--delivery-timeout")
        {
            Description = $"Seconds to wait for a submitted message to appear in the mailbox, 1..{BatchArguments.MaximumDeliveryTimeoutSeconds}.",
            DefaultValueFactory = _ => BatchArguments.DefaultDeliveryTimeoutSeconds,
        };

        Command command = new("replay", "Deliver a corpus that was exported earlier, without generating anything.")
        {
            corpusArgument,
            recipientArgument,
            configurationOption,
            intervalOption,
            deliveryTimeoutOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            ReplayArguments.Parse(
                result.GetValue(corpusArgument) ?? string.Empty,
                result.GetValue(recipientArgument) ?? string.Empty,
                result.GetValue(configurationOption),
                result.GetValue(intervalOption),
                result.GetValue(deliveryTimeoutOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        SyntheticMailContext context,
        ReplayArguments arguments,
        CancellationToken cancellationToken)
    {
        // Read before the corpus is opened, for the reason generation reads them before it generates: a replay that
        // could not have delivered says so first.
        var (account, mailbox) = ExchangeDelivery.ReadAccounts(context, arguments.ConfigurationPath, arguments.Recipient);
        var corpus = ReadCorpus(context, arguments.CorpusPath);

        ReportCorpus(context, arguments, corpus);

        var report = await ExchangeDelivery.DeliverAsync(
            context,
            account,
            mailbox,
            arguments.Recipient,
            corpus.Exchanges,
            arguments.Interval,
            arguments.DeliveryTimeout,
            cancellationToken);

        return ExchangeDelivery.Report(context.Console, arguments.Recipient, report);
    }

    private static ExportedCorpus ReadCorpus(SyntheticMailContext context, string path)
    {
        using var contents = context.OpenCorpus(path);

        return CorpusArchive.Read(contents);
    }

    /// <summary>Says what is about to be delivered, and what produced it.</summary>
    /// <remarks>
    /// The invocation is reported rather than acted on. It is the line that regenerates an equivalent corpus, so a run
    /// filling a mailbox says where its mail came from without anybody unpacking the archive to find out.
    /// </remarks>
    private static void ReportCorpus(SyntheticMailContext context, ReplayArguments arguments, ExportedCorpus corpus)
    {
        var messages = corpus.Exchanges.Sum(exchange => exchange.Count);

        context.Console.WriteError(string.Create(
            CultureInfo.InvariantCulture,
            $"Replaying {messages} messages in {corpus.Exchanges.Count} exchanges from '{arguments.CorpusPath}', waiting up to {arguments.DeliveryTimeout.TotalSeconds:0} seconds per delivery."));

        context.Console.WriteError($"The corpus was generated with: {corpus.Invocation}");
    }
}
