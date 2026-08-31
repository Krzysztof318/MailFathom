// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Judges what one owner asked for over their own mail against what the deployment requires and provides.</summary>
/// <remarks>
/// <para>
/// One rule set for both directions, for the reason the owner-record binder is one binder: the same block arrives as a
/// record somebody is writing, as a record a start reads back, and as an owner's declared section in the deployment's
/// own file, and a rule stated three times is a rule that comes to hold in one of the three places.
/// </para>
/// <para>
/// Every refusal names the deployment setting it is about, because that is the only thing whoever wrote the record can
/// act on: an owner told their write was refused learns which switch the operator holds, and the operator reading the
/// same sentence learns which of theirs the owner was asking about. None of them quotes a value out of the record,
/// which carries an owner's own text.
/// </para>
/// </remarks>
internal static class OwnerSensitiveContentRules
{
    /// <summary>Finds everything about one owner's scanning block that stops it being accepted.</summary>
    /// <param name="owner">What the owner asked for.</param>
    /// <param name="deployment">The deployment's own section, which is what an owner may tighten and never loosen.</param>
    /// <param name="path">How the block is named in the refusal, such as <c>Accounts:0:SensitiveContent</c>.</param>
    /// <returns>One sentence per refusal, empty where the block is one this deployment can serve.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static IReadOnlyList<string> FindRefusals(
        OwnerSensitiveContentOptions owner,
        SensitiveContentOptions deployment,
        string path)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(path);

        return
        [
            .. Enum.GetValues<SensitiveContentScannerKind>()
                .Select(scanner => FindSwitchRefusal(owner, deployment, path, scanner))
                .Where(refusal => refusal is not null)
                .Select(refusal => refusal!),
            .. FindScreeningRefusals(owner, deployment, path),
        ];
    }

    /// <summary>Finds why one owner may not have said what they said about one scanner.</summary>
    /// <remarks>
    /// Two refusals, and only two, because the switch has three states and one of them is always acceptable. Declining
    /// a scanner the deployment requires is refused, since the obligation belongs to whoever holds the mail rather than
    /// to the person it is about. Asking for a scanner the deployment does not provide is refused at the write rather
    /// than left to fail closed at the first message, which would look to the owner like a mailbox that had stopped
    /// working. Today that is the personal-data scanner and only it, because the other one runs inside this process and
    /// is provided by every deployment — which is why the sentence names the analyzer address rather than deriving a
    /// setting from the scanner.
    /// </remarks>
    private static string? FindSwitchRefusal(
        OwnerSensitiveContentOptions owner,
        SensitiveContentOptions deployment,
        string path,
        SensitiveContentScannerKind scanner)
    {
        var asked = owner.For(scanner).Enabled;
        var key = $"{path}:{scanner}:Enabled";

        if (asked is false && deployment.For(scanner).Enabled)
        {
            return $"{key} is false and this deployment requires the {scanner} scanner over every owner's mail. An owner "
                + "may switch a scanner on for their own mail and never off, so remove the setting or state true.";
        }

        if (asked is true && !deployment.ProvidedScanners.Contains(scanner))
        {
            return $"{key} is true and this deployment has configured no personal-data analyzer, so nothing could scan "
                + $"for it. {SensitiveContentOptions.SectionName}:PersonalDataAnalyzer:Endpoint is the deployment "
                + "setting that makes this scanner available, and only an operator can state it.";
        }

        return null;
    }

    /// <summary>Finds why one owner's outgoing-screening list is not one they may state.</summary>
    /// <remarks>
    /// The spelling is judged for the reason the deployment's own list is: an entry naming no scanner would be dropped
    /// in silence and read as a record that screens more than it does. Beyond that the list has to cover what the
    /// deployment screens for, so an owner reading their own record reads what actually stops their mail rather than a
    /// subset that is quietly widened somewhere else.
    /// </remarks>
    private static IEnumerable<string> FindScreeningRefusals(
        OwnerSensitiveContentOptions owner,
        SensitiveContentOptions deployment,
        string path)
    {
        if (owner.ScreenOutgoingMailFor is not { } named)
        {
            yield break;
        }

        var key = $"{path}:{nameof(OwnerSensitiveContentOptions.ScreenOutgoingMailFor)}";
        var accepted = Enum.GetNames<SensitiveContentScannerKind>();

        var unknown = named
            .Where(scanner => !accepted.Contains(scanner, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (unknown.Length > 0)
        {
            // Counted rather than quoted. The entries are an owner's own text, reaching an administrator's refusal and
            // every log of it, so one carrying a newline would put a forged line there and one carrying personal data
            // would put that there — and this same sentence is what a start reports when it reads the record back.
            yield return $"{key} names {unknown.Length} entry this deployment has no scanner for, and every entry is "
                + $"one of the scanners it can switch on: {string.Join(", ", accepted)}.";

            yield break;
        }

        var missing = SensitiveContentPlanMapper.ScreeningScannersOf(deployment)
            .Where(required => !named.Contains(required.ToString(), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missing.Length > 0)
        {
            yield return $"{key} does not name '{string.Join("', '", missing)}', which this deployment stops every "
                + "owner's outgoing mail for. An owner may add to that list and never take from it, so name those "
                + "beside whatever else is wanted, or remove the setting to take the deployment's list as it stands.";
        }
    }
}
