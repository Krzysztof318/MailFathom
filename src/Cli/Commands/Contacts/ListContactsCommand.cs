// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Reads one bounded page of the deployment's contact book.</summary>
/// <remarks>
/// <para>
/// One page per invocation, and the operator asks for the next. There is deliberately no command that walks the whole
/// book: a contact book printed in one call is every correspondent of a person's mailbox on one screen and in one shell
/// history, and paging is what makes reading it an act rather than a side effect of asking who is in it.
/// </para>
/// <para>
/// The order is the deployment's own — the name's comparison form, then the identity — which is total, so a walk serves
/// every contact exactly once. The cursor a page prints is what continues it.
/// </para>
/// </remarks>
internal static class ListContactsCommand
{
    /// <summary>Builds the <c>contact list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Option<string?> originOption = new("--origin")
        {
            Description =
                "Narrow to contacts of one origin: Asserted for the people written down, Collected for the addresses the deployment picked up. Defaults to the whole book.",
        };

        Option<int?> pageSizeOption = new("--page-size")
        {
            Description = "How many contacts to read. Defaults to what the deployment serves.",
        };

        Option<string?> cursorOption = new("--cursor")
        {
            Description = "Continue from where a previous page ended, using the cursor it printed.",
        };

        Command command = new("list", "Read one page of the deployment's contact book.")
        {
            originOption,
            pageSizeOption,
            cursorOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(originOption),
            result.GetValue(pageSizeOption),
            result.GetValue(cursorOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? origin,
        int? pageSize,
        string? cursor,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var page = await new AdminApiClient(transport, context.Console)
            .ReadContactPageAsync(profile.Token, origin, pageSize, cursor, cancellationToken);

        if (page.Contacts is not { Count: > 0 } contacts)
        {
            context.Console.WriteLine(DescribeEmptyPage(origin, cursor));

            return CliExitCode.Success;
        }

        foreach (var contact in contacts)
        {
            ContactOutput.WriteSummary(context.Console, contact);
        }

        if (page.NextCursor is { Length: > 0 } continuation)
        {
            context.Console.WriteLine(string.Empty);
            context.Console.WriteLine($"More contacts follow. Continue with --cursor {continuation}");
        }

        return CliExitCode.Success;
    }

    /// <summary>States that the page held nobody, and what that usually means for the way it was asked.</summary>
    /// <remarks>
    /// A continued walk reaching an empty page is the end of the book rather than an empty book, and telling the two
    /// apart is what stops an operator from reading a completed walk as a deployment that lost its contacts.
    /// </remarks>
    private static string DescribeEmptyPage(string? origin, string? cursor) => (origin, cursor) switch
    {
        (_, { Length: > 0 }) => "That cursor reached the end of the book, so there was nothing further to read.",
        ({ Length: > 0 } narrowed, _) =>
            $"The deployment's contact book holds no {narrowed} contacts. Nothing writes to the book on its own, so an instance nobody has written to holds none at all.",
        _ =>
            "The deployment's contact book is empty. Nothing writes to it on its own, so it holds nobody until somebody is recorded in it.",
    };
}
