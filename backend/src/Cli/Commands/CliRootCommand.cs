// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Commands.Contacts;
using MailFathom.Cli.Commands.Content;
using MailFathom.Cli.Commands.Folders;
using MailFathom.Cli.Commands.Jobs;
using MailFathom.Cli.Commands.Outbox;
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

        // The reading belongs beside the authorization for the reason the group exists at all: both are about the
        // accounts a deployment serves rather than about the deployment itself. "status" reads correctly at this level
        // for the same reason it does under "embedding" — the root one asks whether the stored credential still works,
        // and this one asks whether the mailbox is being kept up to date.
        // The two refreshes sit here rather than under "folder" because both act on an account and merely narrow to a
        // folder, and because what they are about is the mail the account holds rather than the storage one folder
        // occupies. They are two commands rather than one switch for the reason the endpoint serves two routes: the
        // properties have two sources, one of them costs a mailbox over IMAP and the other costs a local read, and a
        // flag deciding which would make a typo the difference between them. The re-derivation is asked for and then
        // watched, so it is two commands again: the deployment carries the walk in the background, and "rederive-status"
        // is the second half — where an operator who walked away comes back to an answer rather than to a scrollback.
        Command mailboxCommand = new("mailbox", "Administer a configured mailbox account.")
        {
            MailboxStatusCommand.Create(context),
            AuthorizeMailboxCommand.Create(context),
            RewindMailboxCommand.Create(context),
            RederiveMailboxCommand.Create(context),
            RederivationStatusCommand.Create(context),
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

        // Reading what has stopped, and the two decisions about one job. Nothing here enqueues, cancels a job that is
        // still on its way, or edits what one points at: what a deployment does in the background is decided by its
        // configuration and its mail, and this group is only where work that already gave up waits for a person.
        Command jobsCommand = new("jobs", "Read the background work that stopped, and decide what becomes of it.")
        {
            DeadLettersCommand.Create(context),
            RetryJobCommand.Create(context),
            DropJobCommand.Create(context),
        };

        // Reading what is queued, and the two decisions about one message. Nothing here composes or sends a message:
        // what leaves this deployment is decided by a tool call or a rule, and this group is where a send that is stuck
        // waits for a person. The two decisions are the only points at which a send is reversible and the only point at
        // which one nothing will attempt again is offered another chance.
        Command outboxCommand = new("outbox", "Read what this deployment is sending, and decide about a message that is stuck.")
        {
            OutboxStatusCommand.Create(context),
            OutboxListCommand.Create(context),
            ShowOutgoingMailCommand.Create(context),
            CancelOutgoingMailCommand.Create(context),
            RequeueOutgoingMailCommand.Create(context),
        };

        // The one group that disposes of mail. A folder's local copy outlives both the switch that stopped mirroring it
        // and the mapping that named it, deliberately, so that no configuration edit can take somebody's mail away —
        // and this is where an operator who means it says so.
        Command folderCommand = new("folder", "Administer what a deployment stores for one of an account's folders.")
        {
            EraseFolderCommand.Create(context),
        };

        // Where a deployment's mail content is held, rather than what it says. The group exists because moving a
        // mailbox out of the database is one long-running act an operator drives in four steps — start it, watch it,
        // stop it while the deployment is busy, set it going again — and each of them is a decision of its own rather
        // than a flag on the others. The fifth is apart from all four: the move copies, and "release" is the separate,
        // irreversible act that frees the copies the database went on holding.
        Command contentCommand = new("content", "Administer where this deployment holds the mail content it stores.")
        {
            MoveContentCommand.Create(context),
            ContentMoveStatusCommand.Create(context),
            PauseContentMoveCommand.Create(context),
            ResumeContentMoveCommand.Create(context),
            ReleaseContentCommand.Create(context),
        };

        // The one group that writes something a person, rather than a mail server, put there. It is also the only place
        // outside "folder erase" where a command disposes of data for good: "delete" is the contact book's data-subject
        // erasure path and says so, and "export" is its access path, both commands rather than seams nothing invokes.
        Command contactCommand = new("contact", "Administer the deployment's contact book.")
        {
            CreateContactCommand.Create(context),
            ShowContactCommand.Create(context),
            ListContactsCommand.Create(context),
            UpdateContactCommand.Create(context),
            AddContactAddressCommand.Create(context),
            RemoveContactAddressCommand.Create(context),
            PromoteContactCommand.Create(context),
            DeleteContactCommand.Create(context),
            DeleteCollectedContactsCommand.Create(context),
            ExportContactCommand.Create(context),
        };

        // The only option the root owns. It governs what the runner does once a command has finished rather than
        // anything a command does, so it is declared once and made recursive rather than added to each of them.
        return new RootCommand($"MailFathom administration tool ({version.Version}).")
        {
            CliOptions.NoLog(),
            LoginCommand.Create(context),
            LogoutCommand.Create(context),
            SwitchCommand.Create(context),
            ProfilesCommand.Create(context),
            StatusCommand.Create(context),
            mailboxCommand,
            embeddingCommand,
            rulesCommand,
            spamCommand,
            jobsCommand,
            outboxCommand,
            folderCommand,
            contentCommand,
            contactCommand,
        };
    }
}
