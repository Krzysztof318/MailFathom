// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli;

/// <summary>The options every command that reaches a deployment shares.</summary>
/// <remarks>
/// <para>
/// A command acts on the profile <c>switch</c> last selected, and <c>--endpoint</c> overrides that for one invocation
/// without changing it. The order is the option, then the environment variable, then the stored default: what an
/// operator typed beats what their shell was told, and both beat what they chose last time.
/// </para>
/// <para>
/// That order is applied here rather than by a configuration pipeline because <c>mfctl</c> composes none. It is a
/// command-line tool with three inputs and no settings file, so the precedence is short enough to state in one method
/// and is the whole of it; the host's own composed configuration governs the service, not the tool that talks to it.
/// </para>
/// </remarks>
internal static class CliOptions
{
    /// <summary>The environment variable naming the deployment, so a shell can state it once for a session.</summary>
    internal const string EndpointVariable = "MAILFATHOM_ENDPOINT";

    /// <summary>The environment variable that turns the invocation log off for a shell session.</summary>
    internal const string LogVariable = "MAILFATHOM_LOG";

    /// <summary>The one value of <see cref="LogVariable" /> that means anything.</summary>
    /// <remarks>
    /// Every other value, including an empty one, leaves the log on. A variable that failed an invocation over a typo
    /// would make a diagnostic aid something that can break a command, and a variable with several spellings for
    /// <c>off</c> is one an operator has to remember the accepted list of.
    /// </remarks>
    internal const string LogOff = "off";

    /// <summary>The name of the option that turns the invocation log off for one invocation.</summary>
    /// <remarks>Named as a constant because the value is read back by name rather than through the instance, which is what lets the option be built per command tree like every other one here.</remarks>
    internal const string NoLogName = "--no-log";

    /// <summary>Builds the option that turns the invocation log off for one invocation.</summary>
    /// <returns>The option.</returns>
    /// <remarks>Recursive, so that it is accepted after the subcommand as well — which is where an operator reaching for it will type it.</remarks>
    internal static Option<bool> NoLog() => new(NoLogName)
    {
        Description = $"Do not record this invocation in the local log. A shell turns it off for a session with ${LogVariable}={LogOff}.",
        Recursive = true,
    };

    /// <summary>Decides whether this invocation is recorded in the local log.</summary>
    /// <param name="parseResult">What the operator typed, parsed.</param>
    /// <param name="logVariable">What <see cref="LogVariable" /> holds, or <see langword="null" /> when it is unset.</param>
    /// <returns><see langword="true" /> when the invocation is recorded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="parseResult" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The same order every other input here follows: what an operator typed beats what their shell was told, and the
    /// default is on. The variable is a parameter rather than read here, so that neither this nor a test driving a whole
    /// invocation depends on the process environment every test in an assembly shares.
    /// </remarks>
    internal static bool RecordsInvocation(ParseResult parseResult, string? logVariable)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        return !parseResult.GetValue<bool>(NoLogName)
            && !StringComparer.OrdinalIgnoreCase.Equals(logVariable, LogOff);
    }

    /// <summary>Builds the option naming which deployment to reach.</summary>
    /// <returns>The option.</returns>
    internal static Option<string?> Endpoint() => new("--endpoint")
    {
        Description = $"The deployment to act on for this invocation: a profile name, or an address such as https://mail.example.test:8443. Defaults to the profile last switched to, or ${EndpointVariable}.",
    };

    /// <summary>Builds the option naming which of a deployment's mail accounts a command acts on.</summary>
    /// <returns>The option.</returns>
    /// <remarks>
    /// Required wherever it appears, and deliberately without a default. A deployment may serve several mailboxes, and
    /// every command taking this either walks one of them or reads what was done to one — so guessing which would be
    /// guessing whose mail an operator meant.
    /// </remarks>
    internal static Option<string> MailAccount() => new("--account")
    {
        Description = "The mail account to act on, as the deployment's configuration names it.",
        Required = true,
    };

    /// <summary>Builds the option naming which folder of an account a command acts on.</summary>
    /// <returns>The option.</returns>
    /// <remarks>
    /// An alias rather than a folder reference, so a role such as <c>role:Junk</c> is not accepted. A role is resolved
    /// through the mapping that declares it, and the folder this names is one whose mapping may have been removed —
    /// which is precisely the case a role could never reach.
    /// </remarks>
    internal static Option<string> MailFolder() => new("--folder")
    {
        Description = "The folder to act on, by the alias the deployment's configuration gave it.",
        Required = true,
    };

    /// <summary>Builds the option narrowing a whole-account command to one of the account's folders.</summary>
    /// <returns>The option.</returns>
    /// <remarks>
    /// Optional where <see cref="MailFolder" /> is required, and that is the whole difference: a command taking this
    /// one acts on an account and the folder merely narrows it, so omitting it means every folder the account holds
    /// mail in rather than a folder nobody named. It is an alias for the same reason the required one is — a role is
    /// resolved through the mapping that declares it, and mail outlives the mapping that brought it in.
    /// </remarks>
    internal static Option<string?> NarrowedMailFolder() => new("--folder")
    {
        Description =
            "Narrow to one folder, by the alias the deployment's configuration gave it. Every folder the account holds mail in when omitted.",
    };

    /// <summary>Builds the option naming the profile a sign-in is remembered under.</summary>
    /// <returns>The option.</returns>
    internal static Option<string?> ProfileName() => new("--name")
    {
        Description = "The name to remember this deployment under. Defaults to its host name.",
    };

    /// <summary>Builds the option continuing a listing from where the previous page ended.</summary>
    /// <returns>The option.</returns>
    /// <remarks>
    /// Declared here rather than per listing because a cursor is one thing: an opaque value the deployment printed,
    /// handed back unread. A description written again per family is the first place the five listings would start
    /// telling an operator different things about the same value.
    /// </remarks>
    internal static Option<string?> Cursor() => new("--cursor")
    {
        Description = "Continue from where a previous page ended, using the cursor it printed.",
    };

    /// <summary>Builds the option bounding how much of a listing one page holds.</summary>
    /// <param name="noun">What the page holds, in the plural, as the listing's own output names it.</param>
    /// <returns>The option.</returns>
    /// <remarks>The noun is the whole of what differs between the listings, and an absent value stays the deployment's own default rather than a number this tool would have to keep in step with it.</remarks>
    internal static Option<int?> PageSize(string noun) => new("--page-size")
    {
        Description = $"How many {noun} to read. Defaults to what the deployment serves.",
    };

    /// <summary>Builds the option an operator states an irreversible act's agreement in the command with.</summary>
    /// <param name="description">What is being agreed to, as the command's own output names it.</param>
    /// <returns>The option.</returns>
    /// <remarks>The prompt is the default and this is the exception, rather than the other way round: a command that assumed consent would be one an operator can only find out about afterwards.</remarks>
    internal static Option<bool> Confirmed(string description) => new("--yes", "-y")
    {
        Description = $"Agree to the {description} without being asked, which is what a scripted run needs.",
    };

    /// <summary>Reports which deployment the operator named for this invocation, if any.</summary>
    /// <param name="configuredEndpoint">What the operator passed to <c>--endpoint</c>, or <see langword="null" />.</param>
    /// <param name="endpointVariable">What <see cref="EndpointVariable" /> holds, or <see langword="null" /> when it is unset.</param>
    /// <returns>A profile name, an address, or <see langword="null" /> to fall back to the stored default.</returns>
    /// <remarks>
    /// The variable is a parameter rather than read here, for the reason <see cref="RecordsInvocation" /> takes its own
    /// one: the process environment is shared by every test in an assembly that runs them in parallel, so a developer
    /// who exported the variable for their own shell would otherwise have command tests resolve a deployment nobody
    /// asked for.
    /// </remarks>
    internal static string? RequestedDeployment(string? configuredEndpoint, string? endpointVariable) =>
        configuredEndpoint is { Length: > 0 } named
            ? named.Trim()
            : endpointVariable is { Length: > 0 } fromEnvironment
                ? fromEnvironment.Trim()
                : null;

    /// <summary>Reads a value as an absolute endpoint address, when it is one.</summary>
    /// <param name="candidate">The value the operator wrote.</param>
    /// <param name="endpoint">The address, when the value is one.</param>
    /// <returns><see langword="true" /> when the value is an absolute HTTP or HTTPS address.</returns>
    /// <remarks>
    /// A value that is not one is read as a profile name rather than repaired into an address: prefixing a scheme onto
    /// a bare host would decide between a protected and an unprotected transport on the operator's behalf, and nothing
    /// about <c>production</c> says which they meant. Nothing can confuse the two, because a profile name is not an
    /// absolute URI.
    /// </remarks>
    internal static bool TryReadAddress(string? candidate, out Uri endpoint)
    {
        if (Uri.TryCreate(candidate?.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
        {
            endpoint = parsed;

            return true;
        }

        endpoint = null!;

        return false;
    }
}
