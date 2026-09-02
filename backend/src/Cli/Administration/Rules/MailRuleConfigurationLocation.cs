// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Rules;

/// <summary>Where a rule is written, which is the answer to every question this tool refuses to answer with a command.</summary>
/// <remarks>
/// <c>mfctl</c> runs rules and reads what they did; it never authors one. A rule lives in the deployment's own
/// configuration so that what an instance will do to a mailbox is reviewable in a diff before it runs, and there is
/// deliberately no command that creates, edits, enables, disables, or deletes one. Naming the section here means every
/// command that has to say so says the same thing.
/// </remarks>
internal static class MailRuleConfigurationLocation
{
    /// <summary>The configuration section a deployment declares its rules in.</summary>
    internal const string SectionName = "MailRules";
}
