// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices;

namespace MailFathom.Cli.Credentials;

/// <summary>Creates the files the command keeps its session in, readable by their owner and nobody else.</summary>
/// <remarks>
/// <para>
/// The mode is set as the file is created rather than applied afterwards, because a file created world-readable and
/// tightened a moment later is readable for the moment in between — and on a shared machine that is the moment that
/// matters.
/// </para>
/// <para>
/// Windows has no mode this can set portably. The per-user profile directory is the boundary there, which is why the
/// mode argument is simply not passed rather than approximated with an access-control list this code would then own.
/// </para>
/// </remarks>
internal static class OwnerOnlyStorage
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Creates a directory the owner alone may enter.</summary>
    /// <param name="directory">The directory path.</param>
    internal static void CreateDirectory(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Directory.CreateDirectory(directory);

            return;
        }

        Directory.CreateDirectory(directory, OwnerOnlyDirectory);
    }

    /// <summary>Opens a file for writing, creating it readable by its owner alone.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The stream, which the caller disposes.</returns>
    internal static FileStream OpenForWriting(string path)
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

    /// <summary>Creates the directory a file lives in, when it names one.</summary>
    /// <param name="path">The file path.</param>
    internal static void CreateDirectoryFor(string path)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            CreateDirectory(directory);
        }
    }
}
