// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Cli.Administration.Configuration;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>How the configuration commands put a reading and a write outcome in front of an operator.</summary>
/// <remarks>
/// Written once because six commands answer the same two questions — what a setting says and where it is decided, and
/// what a write did — and a copy of either per command is how two of them come to phrase a refusal differently. The
/// source is never dropped from a rendering: a value without the layer that supplied it is the reading an operator
/// would act on wrongly.
/// </remarks>
internal static class ConfigurationOutput
{
    /// <summary>Writes the settings a reading covered as a tree, indented by the sections the paths share.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="settings">The settings, ordered by path as the deployment answered them.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A tree rather than a table because a configuration path is a path: the sections are what an operator navigates,
    /// and a flat listing repeats the same three prefixes on every line until the part that differs is the hardest
    /// thing on the screen to find. The full path is still what every other command takes, which is why a leaf carries
    /// its own name rather than a number.
    /// </remarks>
    internal static void WriteTree(CliContext context, IReadOnlyList<EffectiveSettingRecord> settings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        var written = new List<string>();

        foreach (var setting in settings.Where(setting => setting.Path is { Length: > 0 }))
        {
            var segments = setting.Path!.Split(':');

            WriteSections(context, written, segments);

            context.Console.WriteLine(
                $"{Indent(segments.Length - 1)}{segments[^1]} = {setting.Value} [{setting.DescribeSource()}]");
        }
    }

    /// <summary>Writes one setting in full, or says why the deployment reported none at that exact path.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="path">The path the operator asked about.</param>
    /// <param name="setting">The setting the deployment reported, or <see langword="null" /> where it reported none.</param>
    /// <param name="coveredCount">How many settings the deployment reported at or beneath the path.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> or <paramref name="path" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The absent case has two readings and the count is what tells them apart. A path nothing covers is a setting no
    /// source supplies, which is the sentence below. A path several settings sit beneath is a section rather than a
    /// setting, and saying no source supplies it would be false in the one case this command can already see is false:
    /// the reading it discarded is the proof. Naming the count and the command that reads a section is what an operator
    /// who typed a section does next, and what a misspelling never produces.
    /// </remarks>
    internal static void WriteSetting(
        CliContext context,
        string path,
        EffectiveSettingRecord? setting,
        int coveredCount)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(path);

        if (setting is null)
        {
            context.Console.WriteLine(coveredCount > 0
                ? $"No source supplies {path} itself, and {coveredCount} setting{(coveredCount == 1 ? " sits" : "s sit")} beneath it. Read them with 'mfctl config show {path}'."
                : $"No source supplies {path}. The deployment reads whatever the setting's own default is, which is stated in the configuration reference rather than here.");

            return;
        }

        CliDetails details = new();
        details.Add("Setting", setting.Path ?? path);
        details.Add("Value", setting.Value ?? string.Empty);
        details.Add("Source", setting.DescribeSource());

        context.Console.Write(details);

        if (setting.Redacted)
        {
            context.Console.WriteLine(
                "The setting bears a secret, so the value is redacted wherever it is read back. What it holds is a reference to where the material is kept, never the material.");
        }
    }

    /// <summary>Reports what a write did, and says what the invocation should end with.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="answer">What the deployment reported.</param>
    /// <returns>The exit code, which is a failure exactly when the deployment refused the write.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A write that changed nothing is a success rather than a failure, because the deployment already reads as the
    /// operator asked for and a script repeating a command is not a script that has gone wrong. A refusal is a failure,
    /// including the one about a setting an override supplies — that write was meant and did not happen.
    /// </remarks>
    internal static int ReportWrite(CliContext context, ConfigurationWriteAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(answer);

        if (!answer.Committed)
        {
            return Report(context, answer);
        }

        context.Console.WriteLine(
            $"Committed persisted configuration version {answer.Version.ToString(CultureInfo.InvariantCulture)}.");

        foreach (var change in answer.Changes ?? [])
        {
            context.Console.WriteLine($"  {change.Path}");
            context.Console.WriteLine($"    before: {SettingChangeRecord.Describe(change.Before)}");
            context.Console.WriteLine($"    now:    {SettingChangeRecord.Describe(change.After)}");
        }

        return CliExitCode.Success;
    }

    private static int Report(CliContext context, ConfigurationWriteAnswer answer)
    {
        var refused = answer.Code is not null;

        foreach (var message in answer.DescribeRefusal())
        {
            if (refused)
            {
                context.Console.WriteError(message);
            }
            else
            {
                context.Console.WriteLine(message);
            }
        }

        if (answer.Code == ConfigurationWriteAnswer.WriteShadowed)
        {
            context.Console.WriteNotice(
                "Add --even-if-shadowed to persist the value anyway, which is what staging a setting beneath an override you are about to remove means.");
        }

        return refused ? CliExitCode.Failure : CliExitCode.Success;
    }

    /// <summary>Writes the section headings a path introduces that the path before it did not.</summary>
    private static void WriteSections(CliContext context, List<string> written, string[] segments)
    {
        foreach (var depth in Enumerable.Range(0, segments.Length - 1))
        {
            var section = string.Join(':', segments[..(depth + 1)]);

            if (written.Contains(section, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            written.Add(section);
            context.Console.WriteLine($"{Indent(depth)}{segments[depth]}:");
        }
    }

    private static string Indent(int depth) => new(' ', depth * 2);
}
