// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices;
using System.Text.Json;

namespace MailFathom.Cli;

/// <summary>Where the command remembers the credential an operator signed in with.</summary>
/// <remarks>
/// <para>
/// This is the command's own session and nothing else. It never reads or writes the service's configuration, its
/// database, or its secret store — administering a deployment happens over HTTP, and this file exists only so that
/// signing in once is enough for the commands that follow.
/// </para>
/// <para>
/// One entry per endpoint, keyed by the address the operator signed in to, so a workstation administering a staging and
/// a production deployment holds both without either overwriting the other.
/// </para>
/// <para>
/// The file is written owner-only where the platform expresses that. On Linux that is mode <c>600</c>, set on the file
/// and on the directory, and set before anything is written to it rather than after — a file created world-readable and
/// tightened afterwards is readable for the moment in between. Windows has no equivalent this can set portably, and the
/// per-user profile directory is already the boundary there.
/// </para>
/// </remarks>
internal sealed class CredentialStore
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string storePath;

    /// <summary>Initializes a store over an explicit file, which is what a test supplies.</summary>
    /// <param name="storePath">The file the credentials are read from and written to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storePath" /> is <see langword="null" />.</exception>
    internal CredentialStore(string storePath)
    {
        ArgumentNullException.ThrowIfNull(storePath);

        this.storePath = storePath;
    }

    /// <summary>Reports where the store lives for the operator running the command.</summary>
    /// <returns>The absolute path of the credentials file.</returns>
    /// <remarks>
    /// <see cref="Environment.SpecialFolder.ApplicationData" /> resolves to <c>$XDG_CONFIG_HOME</c> or
    /// <c>~/.config</c> on Linux and to <c>%APPDATA%</c> on Windows, so one call gives the right per-user location on
    /// both without a platform branch here.
    /// </remarks>
    internal static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "MailFathom",
        "credentials.json");

    /// <summary>Reads the credential stored for one endpoint.</summary>
    /// <param name="endpoint">The endpoint address the credential was stored under.</param>
    /// <returns>The stored credential, or <see langword="null" /> when the operator has not signed in to it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the file exists but cannot be read as a credential store.</exception>
    internal StoredCredential? Find(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return this.Read().TryGetValue(KeyFor(endpoint), out var credential) ? credential : null;
    }

    /// <summary>Stores the credential for one endpoint, replacing any the operator signed in with before.</summary>
    /// <param name="endpoint">The endpoint address.</param>
    /// <param name="credential">The credential to remember.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the store cannot be written.</exception>
    internal void Save(Uri endpoint, StoredCredential credential)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credential);

        var credentials = this.Read();
        credentials[KeyFor(endpoint)] = credential;

        this.Write(credentials);
    }

    /// <summary>Forgets the credential stored for one endpoint.</summary>
    /// <param name="endpoint">The endpoint address.</param>
    /// <returns><see langword="true" /> when a credential was removed, <see langword="false" /> when none was stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the store cannot be written.</exception>
    internal bool Remove(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var credentials = this.Read();

        if (!credentials.Remove(KeyFor(endpoint)))
        {
            return false;
        }

        this.Write(credentials);

        return true;
    }

    /// <summary>Keys an endpoint the way two spellings of one address still find each other.</summary>
    /// <remarks>The authority without a trailing slash, lowercased by the URI parser, so <c>https://Host:8443/</c> and <c>https://host:8443</c> are one entry rather than two.</remarks>
    private static string KeyFor(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Authority);

    private Dictionary<string, StoredCredential> Read()
    {
        if (!File.Exists(this.storePath))
        {
            return new Dictionary<string, StoredCredential>(StringComparer.Ordinal);
        }

        try
        {
            using var contents = File.OpenRead(this.storePath);

            return JsonSerializer.Deserialize(contents, CliJsonContext.Default.DictionaryStringStoredCredential)
                ?? new Dictionary<string, StoredCredential>(StringComparer.Ordinal);
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new CliFailure(
                $"The credential store at {this.storePath} could not be read. Remove the file to sign in again.",
                failure);
        }
    }

    private void Write(Dictionary<string, StoredCredential> credentials)
    {
        try
        {
            var directory = Path.GetDirectoryName(this.storePath);

            if (!string.IsNullOrEmpty(directory))
            {
                CreateOwnerOnlyDirectory(directory);
            }

            // Created owner-only rather than created and then tightened, so the file is never briefly readable by
            // anything else on the machine. On Windows the mode argument is ignored and the profile directory is the
            // boundary instead.
            using (var contents = OpenOwnerOnlyForWriting(this.storePath))
            {
                JsonSerializer.Serialize(contents, credentials, CliJsonContext.Default.DictionaryStringStoredCredential);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"The credential store at {this.storePath} could not be written.", failure);
        }
    }

    private static void CreateOwnerOnlyDirectory(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Directory.CreateDirectory(directory);

            return;
        }

        Directory.CreateDirectory(directory, OwnerOnlyDirectory);
    }

    private static FileStream OpenOwnerOnlyForWriting(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            options.UnixCreateMode = OwnerOnlyFile;
        }

        return new FileStream(path, options);
    }
}
