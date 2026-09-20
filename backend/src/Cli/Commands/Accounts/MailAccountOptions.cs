// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Editing;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>How a mail account is named, and how a declaration is read from the file an invocation names.</summary>
internal static class MailAccountOptions
{
    /// <summary>Builds the option naming which mail account a command acts on.</summary>
    /// <returns>The option.</returns>
    /// <remarks>Required rather than settled from a listing, unlike a user: a deployment serving one person routinely holds several accounts for them, so there is no single account to fall back on.</remarks>
    internal static Option<Guid> Account() => new("--account")
    {
        Description = "The mail account, by the identifier the deployment generated for it. 'mfctl account list' shows them.",
        Required = true,
    };

    /// <summary>Builds the option naming the file a declaration is read from.</summary>
    /// <returns>The option.</returns>
    internal static Option<FileInfo> DeclarationFile() => new("--from-file", "-f")
    {
        Description =
            "The object declaring the mail account: its EmailAddress, its DisplayName, the server settings, and a reference to the credential. "
            + "JSON, or YAML where the file is named .yaml or .yml.",
        Required = true,
    };

    /// <summary>Reads the declaration an invocation named, as the JSON the deployment is handed.</summary>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The declaration, as JSON.</returns>
    /// <exception cref="CliFailure">Thrown when there is no such file, it cannot be read, it is empty, or it is YAML saying something JSON cannot.</exception>
    /// <remarks>
    /// <para>A path that is a directory, unreadable by this account, or on a filesystem that failed mid-read is something the operator acts on, so it is reported as a sentence naming the path rather than as a stack trace.</para>
    /// <para>The extension decides the format, as it does for the configuration files a deployment provisions: a <c>.yaml</c> or <c>.yml</c> file is converted to JSON under the YAML 1.2 core schema, and anything else is sent as the operator wrote it.</para>
    /// </remarks>
    internal static async Task<string> ReadDeclarationAsync(FileInfo? file, CancellationToken cancellationToken)
    {
        if (file is null || !file.Exists)
        {
            throw new CliFailure($"There is no file at {file?.FullName ?? "the path given"} to read the mail account from.");
        }

        string declaration;

        try
        {
            declaration = await File.ReadAllTextAsync(file.FullName, cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"{file.FullName} could not be read, so nothing was written.", failure);
        }

        if (string.IsNullOrWhiteSpace(declaration))
        {
            throw new CliFailure($"{file.FullName} is empty, so it declares no mail account.");
        }

        return WrittenInYaml(file) ? AsJson(declaration, file) : declaration;
    }

    /// <summary>Reports whether the file names itself as YAML.</summary>
    private static bool WrittenInYaml(FileInfo file) =>
        file.Extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
        || file.Extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts a YAML declaration to the JSON the deployment is handed.</summary>
    /// <exception cref="CliFailure">Thrown when the file is not acceptable YAML, or says something JSON cannot.</exception>
    private static string AsJson(string declaration, FileInfo file)
    {
        string? converted;

        try
        {
            converted = YamlDocumentView.ReadBack(declaration);
        }
        catch (FormatException refusal)
        {
            throw new CliFailure(
                $"The YAML in {file.FullName} could not be read as a JSON document, so nothing was written. {refusal.Message}",
                refusal);
        }

        return converted ?? throw new CliFailure($"{file.FullName} holds no document, so it declares no mail account.");
    }
}
