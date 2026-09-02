// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>Where a run's configuration is read from: the named local file, or the machine's <c>dotnet user-secrets</c> store.</summary>
/// <remarks>
/// <para>
/// A developer who already keeps development credentials in the user-secrets store should not have to write a second
/// copy of one into the checkout, so a run whose local file is absent reads the store instead. The store is a plain
/// JSON file at a path derived from this project's <c>UserSecretsId</c>, which is why no configuration package is
/// referenced for it: reading it is opening that file, and the two readers beside this type already know how to turn
/// one into an account or a provider.
/// </para>
/// <para>
/// <c>dotnet user-secrets set</c> writes a key exactly as it was typed, so <c>mailbox:host</c> arrives as a key with a
/// colon in it rather than as a member of a <c>mailbox</c> object, and every value it writes is a string.
/// <see cref="Nest" /> turns the first back into the document the readers expect and leaves a hand-written nested
/// object alone; the second is why the serialization contract reads a number from a string.
/// </para>
/// </remarks>
internal static class ConfigurationSource
{
    /// <summary>The identifier the store is kept under.</summary>
    /// <remarks>
    /// Stated here as well as in <c>SyntheticMail.csproj</c>, which is what the <c>dotnet user-secrets</c> command
    /// itself reads. Recovering it from the generated assembly attribute instead would mean referencing a
    /// configuration package for one constant string.
    /// </remarks>
    internal const string UserSecretsId = "mailfathom-synthetic-mail-5c4b9288-c758-4871-8c58-d869809d8808";

    /// <summary>What a message about the store names, so a refusal points at something a developer can act on.</summary>
    internal static string UserSecretsOrigin => $"the user secrets of '{UserSecretsId}'";

    /// <summary>Reports where <c>dotnet user-secrets</c> keeps this project's store.</summary>
    /// <returns>The absolute path of the store's file, whether or not anything has been set in it.</returns>
    /// <remarks>Resolved the way the tooling resolves it: under <c>%APPDATA%</c> on Windows and under the home directory anywhere else.</remarks>
    internal static string UserSecretsPath()
    {
        var root = Environment.GetEnvironmentVariable("APPDATA") is { Length: > 0 } roaming
            ? Path.Combine(roaming, "Microsoft", "UserSecrets")
            : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? AppContext.BaseDirectory, ".microsoft", "usersecrets");

        return Path.Combine(root, UserSecretsId, "secrets.json");
    }

    /// <summary>Opens what the run reads, preferring the named file and falling back to the store.</summary>
    /// <param name="path">The local file the command was pointed at.</param>
    /// <returns>The contents and what a failure about them names, or <see langword="null" /> when neither exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when what exists could not be opened or is not a JSON object.</exception>
    /// <remarks>
    /// The file wins, because it is the one a developer pointed the command at; the store is what a checkout that has
    /// never written one falls back to. Nothing is merged across the two: a half-configured run refuses with a message
    /// naming one origin rather than reporting keys from two.
    /// </remarks>
    internal static (Stream Contents, string Origin)? Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (File.Exists(path))
        {
            return (OpenFile(path), path);
        }

        var store = UserSecretsPath();

        return File.Exists(store) ? (NestFile(store), UserSecretsOrigin) : null;
    }

    /// <summary>Turns the flat keys the user-secrets tooling writes into the nested document the readers deserialize.</summary>
    /// <param name="flattened">The store's contents.</param>
    /// <returns>The same values with every colon-separated key expanded into a block.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flattened" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the contents are not a JSON object.</exception>
    internal static Stream Nest(Stream flattened)
    {
        ArgumentNullException.ThrowIfNull(flattened);

        JsonObject? document;

        try
        {
            document = JsonNode.Parse(flattened) as JsonObject;
        }
        catch (Exception failure) when (failure is JsonException or IOException)
        {
            throw new SyntheticMailFailure($"{UserSecretsOrigin} could not be read as JSON: {failure.Message}", failure);
        }

        if (document is null)
        {
            throw new SyntheticMailFailure($"{UserSecretsOrigin} is not a JSON object.");
        }

        var nested = new JsonObject();

        foreach (var (key, value) in document)
        {
            var segments = key.Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                continue;
            }

            var block = nested;

            for (var depth = 0; depth < segments.Length - 1; depth++)
            {
                if (block[segments[depth]] is not JsonObject inner)
                {
                    inner = [];
                    block[segments[depth]] = inner;
                }

                block = inner;
            }

            block[segments[^1]] = value?.DeepClone();
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(nested.ToJsonString()));
    }

    private static Stream NestFile(string path)
    {
        using var contents = OpenFile(path);

        return Nest(contents);
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
}
