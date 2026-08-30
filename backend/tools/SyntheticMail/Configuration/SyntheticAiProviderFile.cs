// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>Where the AI provider is read from, and the one place a run can learn a provider key.</summary>
/// <remarks>
/// <para>
/// The key never reaches an argument. A key typed on a command line lands in the shell history and in the process
/// list of a shared machine, and this repository is public enough that the pattern would be copied — so the key, the
/// model, and the endpoint come from a local file that <c>.gitignore</c> covers as <c>*.local.json</c>, beside the
/// sending account file and on the same terms.
/// </para>
/// <para>
/// Every refusal here names the file and the key to set, for the reason <see cref="SendingAccountFile" /> gives: a
/// tool nobody has configured yet is the ordinary first experience of it, so "what do I write, and where" is the
/// whole content of the failure rather than something to go and look up.
/// </para>
/// </remarks>
internal static class SyntheticAiProviderFile
{
    /// <summary>The name the file carries beside the built command.</summary>
    internal const string FileName = "synthetic-mail-ai.local.json";

    /// <summary>Reports where the command looks when nothing was named.</summary>
    /// <returns>The absolute path of the AI provider file.</returns>
    /// <remarks>
    /// Beside the executable rather than relative to the working directory, so the command finds the same file however
    /// it was started. The project copies it there when a developer has written one.
    /// </remarks>
    internal static string DefaultPath() => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Reads the provider configuration, refusing anything incomplete.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The configuration, with every value checked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the file is missing, unreadable, or incomplete, with a message naming what to write.</exception>
    internal static AiProviderConfiguration Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new SyntheticMailFailure(
                $"No AI provider is configured. Write '{path}' as {{ \"apiKey\": \"…\", \"model\": \"…\" }} and treat the key as one that reaches a third party: this mode sends the generation prompt to the endpoint and reads the message content back. The file is git-ignored.");
        }

        using var contents = OpenFile(path);

        return ReadFrom(contents, path);
    }

    /// <summary>Reads the provider configuration from an already-open file, which is where every check on its contents happens.</summary>
    /// <param name="contents">The file's contents.</param>
    /// <param name="origin">What the failures name, so a message points at a path rather than at a stream.</param>
    /// <returns>The configuration, with every value checked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the contents are not a complete provider configuration.</exception>
    /// <remarks>Separate from <see cref="Read" /> so every rule about what the file must say is exercised without a test writing one.</remarks>
    internal static AiProviderConfiguration ReadFrom(Stream contents, string origin)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(origin);

        var document = Deserialize(contents, origin);

        return new AiProviderConfiguration(
            Required(document.ApiKey, "apiKey", origin),
            Required(document.Model, "model", origin),
            ParseEndpoint(document.Endpoint, origin));
    }

    private static FileStream OpenFile(string path)
    {
        try
        {
            return File.OpenRead(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new SyntheticMailFailure($"'{path}' could not be opened: {failure.Message}", failure);
        }
    }

    private static AiProviderConfigurationDocument Deserialize(Stream contents, string origin)
    {
        try
        {
            return JsonSerializer.Deserialize(contents, SyntheticMailJsonContext.Default.AiProviderConfigurationDocument)
                ?? throw new SyntheticMailFailure($"'{origin}' holds no AI provider configuration.");
        }
        catch (Exception failure) when (failure is JsonException or IOException)
        {
            throw new SyntheticMailFailure($"'{origin}' could not be read as an AI provider configuration: {failure.Message}", failure);
        }
    }

    private static string Required(string? value, string key, string path) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new SyntheticMailFailure($"'{key}' is not set in '{path}'.")
            : value;

    /// <summary>Resolves the address the run reaches, refusing one the key would travel over unsecured.</summary>
    /// <remarks>
    /// The same refusal the service's provider endpoints apply: a key is presented as a bearer token in a header, so
    /// an endpoint that cannot secure the connection is refused rather than downgraded to. An address that is not
    /// written is the provider's own default rather than an error, because the default is the one most of a developer's
    /// runs want and naming it is what the option exists to change.
    /// </remarks>
    private static Uri? ParseEndpoint(string? endpoint, string path)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps
            ? address
            : throw new SyntheticMailFailure(
                $"'endpoint' in '{path}' is '{endpoint}', which is not an absolute https address. The key travels in a header, so there is no unsecured option.");
    }
}
