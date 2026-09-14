// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Judges what one account asked for over the mail in it against what the deployment requires and provides.</summary>
/// <remarks>
/// <para>
/// One rule set however the block arrives, for the reason the record binder is one binder: the same block reaches here
/// as an account somebody is writing and as an account composed into the record a start reads, and a rule stated twice
/// is a rule that comes to hold in one of the two places. A record this deployment already holds reaches none of these
/// rules — <see cref="UserSettings.UserRecordArrival" /> holds why — and is composed to the stricter answer instead.
/// </para>
/// <para>
/// Every refusal names the deployment setting it is about, because that is the only thing whoever wrote the record can
/// act on: somebody told their write was refused learns which switch the operator holds, and the operator reading the
/// same sentence learns which of theirs was being asked about. None of them quotes a value out of the record, which
/// carries text somebody else wrote.
/// </para>
/// </remarks>
internal static class MailAccountSensitiveContentRules
{
    /// <summary>Finds everything about one account's scanning block that stops it being accepted.</summary>
    /// <param name="account">What the account asked for.</param>
    /// <param name="deployment">The deployment's own section, which is what an account may tighten and never loosen.</param>
    /// <param name="path">How the block is named in the refusal, such as <c>MailAccounts:0:SensitiveContent</c>.</param>
    /// <returns>One sentence per refusal, empty where the block is one this deployment can serve.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static IReadOnlyList<string> FindRefusals(
        MailAccountSensitiveContentOptions account,
        SensitiveContentOptions deployment,
        string path)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(path);

        return
        [
            .. Enum.GetValues<SensitiveContentScannerKind>()
                .Select(scanner => FindSwitchRefusal(account, deployment, path, scanner))
                .Where(refusal => refusal is not null)
                .Select(refusal => refusal!),
            .. FindScreeningRefusals(account, deployment, path),
        ];
    }

    /// <summary>Finds why one account may not state what it states about one scanner.</summary>
    /// <remarks>
    /// Two refusals, and only two, because the switch has three states and one of them is always acceptable. Declining
    /// a scanner the deployment requires is refused, since the obligation belongs to whoever holds the mail rather than
    /// to the people the mailbox is about. Asking for a scanner the deployment does not provide is refused at the write
    /// rather than left to fail closed at the first message, which would look like a mailbox that had stopped
    /// working. Today that is the personal-data scanner and only it, because the other one runs inside this process and
    /// is provided by every deployment — which is why the sentence names the analyzer address rather than deriving a
    /// setting from the scanner.
    /// </remarks>
    private static string? FindSwitchRefusal(
        MailAccountSensitiveContentOptions account,
        SensitiveContentOptions deployment,
        string path,
        SensitiveContentScannerKind scanner)
    {
        var asked = account.For(scanner).Enabled;
        var key = $"{path}:{scanner}:Enabled";

        if (asked is false && deployment.For(scanner).Enabled)
        {
            return $"{key} is false and this deployment requires the {scanner} scanner over every account's mail. An account "
                + "may switch a scanner on for its own mail and never off, so remove the setting or state true.";
        }

        if (asked is true && !deployment.ProvidedScanners.Contains(scanner))
        {
            return $"{key} is true and this deployment has configured no personal-data analyzer, so nothing could scan "
                + $"for it. {SensitiveContentOptions.SectionName}:PersonalDataAnalyzer:Endpoint is the deployment "
                + "setting that makes this scanner available, and only an operator can state it.";
        }

        return null;
    }

    /// <summary>Finds why one account's outgoing-screening list is not one it may state.</summary>
    /// <remarks>
    /// The spelling is judged for the reason the deployment's own list is: an entry naming no scanner would be dropped
    /// in silence and read as a record that screens more than it does. Beyond that the list has to cover what the
    /// deployment screens for, so whoever reads an account's record reads what actually stops its mail rather than a
    /// subset that is quietly widened somewhere else.
    /// </remarks>
    private static IEnumerable<string> FindScreeningRefusals(
        MailAccountSensitiveContentOptions account,
        SensitiveContentOptions deployment,
        string path)
    {
        if (account.ScreenOutgoingMailFor is not { } named)
        {
            yield break;
        }

        var key = $"{path}:{nameof(MailAccountSensitiveContentOptions.ScreenOutgoingMailFor)}";
        var accepted = Enum.GetNames<SensitiveContentScannerKind>();

        var unknown = named
            .Where(scanner => !accepted.Contains(scanner, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (unknown.Length > 0)
        {
            // Counted rather than quoted. The entries are an account's own text, reaching an administrator's refusal on a
            // record write and a start's refusal of an account's section in the deployment's own file, so one carrying a
            // newline would put a forged line in every log of either and one carrying personal data would put that there.
            var counted = unknown.Length == 1 ? "1 entry" : $"{unknown.Length} entries";

            yield return $"{key} names {counted} this deployment has no scanner for, and every entry is "
                + $"one of the scanners it can switch on: {string.Join(", ", accepted)}.";

            yield break;
        }

        var missing = SensitiveContentPlanMapper.ScreeningScannersOf(deployment)
            .Where(required => !named.Contains(required.ToString(), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missing.Length > 0)
        {
            yield return $"{key} does not name '{string.Join("', '", missing)}', which this deployment stops every "
                + "account's outgoing mail for. An account may add to that list and never take from it, so name those "
                + "beside whatever else is wanted, or remove the setting to take the deployment's list as it stands.";
        }
    }
}
