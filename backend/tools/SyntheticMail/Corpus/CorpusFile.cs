// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Corpus;

/// <summary>Where a corpus is written and where one is read from, and the only place this tool touches those paths.</summary>
/// <remarks>
/// The file system is behind this type for the reason the credential file's reader is behind its own: what a run does
/// with a corpus is asserted without one, and a refusal names the path and what to do about it rather than surfacing
/// whatever the framework raised.
/// </remarks>
internal static class CorpusFile
{
    /// <summary>Opens the archive an export writes, refusing to write over one that already exists.</summary>
    /// <param name="path">Where to write the corpus.</param>
    /// <returns>The stream, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when a file is already there, or the path could not be written.</exception>
    /// <remarks>
    /// Refused rather than replaced, because generating a corpus costs money and a run that silently overwrote one
    /// would be the way a corpus somebody paid for is lost — to a repeated command, a shell history entry, or a
    /// mistyped name. Deleting the old file is a deliberate act and stays the developer's.
    /// </remarks>
    internal static Stream Create(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (File.Exists(path))
        {
            throw new SyntheticMailFailure($"'{path}' already exists, and a corpus is never written over one: name another file, or delete that one.");
        }

        try
        {
            // The directory a corpus is named into is created rather than required, because the one this repository
            // keeps its corpora in exists only once somebody has exported one.
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            return File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new SyntheticMailFailure($"'{path}' could not be written: {failure.Message}", failure);
        }
    }

    /// <summary>Opens the archive a replay reads.</summary>
    /// <param name="path">The corpus to read.</param>
    /// <returns>The stream, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the file is missing or could not be opened.</exception>
    internal static Stream Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new SyntheticMailFailure($"'{path}' does not exist: name the corpus to replay, or export one first.");
        }

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
