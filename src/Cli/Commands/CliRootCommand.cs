// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Commands.Folders;
using MailFathom.Cli.Commands.Rules;
using MailFathom.Cli.Commands.Spam;
using MailFathom.Versioning;

namespace MailFathom.Cli.Commands;

/// <summary>The commands <c>mfctl</c> publishes.</summary>
/// <remarks>
/// Built here rather than in the entry point so a test can parse an argument list against the real command tree. What
/// the command accepts is part of its contract, and a contract nothing can exercise is one that drifts.
/// </remarks>
internal static class CliRootCommand
{
    /// <summary>The name the published binary carries, as an operator types it.</summary>
    /// <remarks>
    /// Written here rather than read from the running process, because it appears in guidance a failing command prints
    /// — "run <c>mfctl login</c>" — and that has to name the command as it is distributed even when the file has been
    /// renamed on the way to somebody's <c>PATH</c>. The assembly name in <c>Cli.csproj</c> is the other half.
    /// </remarks>
    internal const string CommandName = "mfctl";

    /// <summary>Builds the command tree.</summary>
    /// <param name="context">What the commands need from their surroundings.</param>
    /// <returns>The root command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static RootCommand Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var version = StampedAssemblyVersion.ReadFrom(typeof(CliRootCommand).Assembly);

        Command mailboxCommand = new("mailbox", "Administer a configured mailbox account.")
        {
            AuthorizeMailboxCommand.Create(context),
        };

        // A group of its own rather than three commands at the root, because "status" already means something here:
        // the root one asks whether the stored credential still works, and this one asks whether semantic search does.
        // Both are worth having and neither should have to be renamed for the other.
        Command embeddingCommand = new("embedding", "Administer the deployment's embedding profile.")
        {
            EmbeddingStatusCommand.Create(context),
            ActivateEmbeddingCommand.Create(context),
            CancelEmbeddingReindexCommand.Create(context),
        };

        // Reading and running, and nothing that writes. A rule lives in the deployment's configuration so that what an
        // instance will do to a mailbox is reviewable in a diff before it runs, so there is deliberately no command here
        // that creates, edits, enables, disables, or deletes one — and there will not be.
        Command rulesCommand = new("rules", "Read the deployment's mail rules, run them, and read what they did.")
        {
            ListRulesCommand.Create(context),
            ShowRuleCommand.Create(context),
            RunRulesCommand.Create(context),
            RuleRunStatusCommand.Create(context),
            RuleHistoryCommand.Create(context),
        };

        // Running and reading, and nothing that writes a setting. Whether mail is classified at all, what a scanner is
        // judged by, and what happens to junk are configuration for the reason a rule is, so there is deliberately no
        // command here that switches any of them — what an operator does from here is apply them to the mail they
        // already have, and find out what was decided.
        Command spamCommand = new("spam", "Classify the mail a deployment already holds, and read what it concluded.")
        {
            ClassifyMailCommand.Create(context),
            ClassificationRunStatusCommand.Create(context),
            ClassificationsCommand.Create(context),
        };

        // The one group that disposes of mail. A folder's local copy outlives both the switch that stopped mirroring it
        // and the mapping that named it, deliberately, so that no configuration edit can take somebody's mail away —
        // and this is where an operator who means it says so.
        Command folderCommand = new("folder", "Administer what a deployment stores for one of an account's folders.")
        {
            EraseFolderCommand.Create(context),
        };

        return new RootCommand($"MailFathom administration tool ({version.Version}).")
        {
            LoginCommand.Create(context),
            LogoutCommand.Create(context),
            SwitchCommand.Create(context),
            ProfilesCommand.Create(context),
            StatusCommand.Create(context),
            mailboxCommand,
            embeddingCommand,
            rulesCommand,
            spamCommand,
            folderCommand,
        };
    }
}
