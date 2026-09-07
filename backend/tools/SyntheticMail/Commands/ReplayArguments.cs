// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.SyntheticMail.Configuration;
using MimeKit;

namespace MailFathom.SyntheticMail.Commands;

/// <summary>One replay, with every value checked and every default already resolved.</summary>
/// <param name="CorpusPath">The corpus to deliver.</param>
/// <param name="Recipient">The mailbox it is delivered into.</param>
/// <param name="ConfigurationPath">Where the two accounts are read from.</param>
/// <param name="Interval">How long the run waits between two submissions.</param>
/// <param name="DeliveryTimeout">How long it waits for a submitted message to appear in the mailbox.</param>
/// <remarks>
/// Five values, and none of them decides what the mail says. Everything a corpus is was decided when it was generated,
/// which is the whole point of having exported it.
/// </remarks>
internal sealed record ReplayArguments(
    string CorpusPath,
    MailboxAddress Recipient,
    string ConfigurationPath,
    TimeSpan Interval,
    TimeSpan DeliveryTimeout)
{
    /// <summary>Checks one invocation and resolves what it left unsaid.</summary>
    /// <param name="corpus">The corpus named on the command line.</param>
    /// <param name="recipient">The address given as the argument.</param>
    /// <param name="configurationPath">Where to read the accounts, or <see langword="null" /> for the default.</param>
    /// <param name="intervalMilliseconds">How long to wait between two submissions.</param>
    /// <param name="deliveryTimeoutSeconds">How long to wait for a submitted message to appear in the mailbox.</param>
    /// <returns>The checked invocation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="corpus" /> or <paramref name="recipient" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when a value is missing or outside its bounds, with a message naming the option.</exception>
    internal static ReplayArguments Parse(
        string corpus,
        string recipient,
        string? configurationPath,
        int intervalMilliseconds,
        int deliveryTimeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(recipient);

        if (string.IsNullOrWhiteSpace(corpus))
        {
            throw new SyntheticMailFailure("'replay' names the corpus to deliver: write the path of an archive '--export' wrote.");
        }

        return new ReplayArguments(
            corpus,
            MailboxAddress.TryParse(recipient, out var parsed)
                ? parsed
                : throw new SyntheticMailFailure($"'{recipient}' is not a mail address."),
            string.IsNullOrWhiteSpace(configurationPath) ? SendingAccountFile.DefaultPath() : configurationPath,
            TimeSpan.FromMilliseconds(Bounded(intervalMilliseconds, 0, BatchArguments.MaximumIntervalMilliseconds, "--interval")),
            TimeSpan.FromSeconds(Bounded(deliveryTimeoutSeconds, 1, BatchArguments.MaximumDeliveryTimeoutSeconds, "--delivery-timeout")));
    }

    private static int Bounded(int value, int lowest, int highest, string option) =>
        value < lowest || value > highest
            ? throw new SyntheticMailFailure(
                string.Create(CultureInfo.InvariantCulture, $"'{option} {value}' is outside {lowest}..{highest}."))
            : value;
}
