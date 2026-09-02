// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Serves configuration files a test composed, so a layering claim needs no directory on disk.</summary>
/// <remarks>
/// A JSON configuration source reads its file through an <see cref="IFileProvider" />, which is the seam that lets a
/// test state the file's content directly instead of writing it, watching it, and deleting it again. Nothing here
/// changes after the test arranged it, so <see cref="Watch" /> returns a token that never fires: a source composed
/// over this provider reloads exactly when something republishes it, which is what makes the layering the test asserts
/// the only thing deciding a value.
/// </remarks>
internal sealed class InMemoryConfigurationFileProvider : IFileProvider
{
    private readonly Dictionary<string, string> filesByName = new(StringComparer.Ordinal);

    /// <summary>Adds a file this provider serves, replacing one already served under that name.</summary>
    /// <param name="fileName">The name a configuration source reads it by.</param>
    /// <param name="content">The file's whole content.</param>
    /// <returns>The same provider, so files can be stated one after another.</returns>
    public InMemoryConfigurationFileProvider WithFile(string fileName, string content)
    {
        this.filesByName[fileName] = content;

        return this;
    }

    /// <inheritdoc />
    public IFileInfo GetFileInfo(string subpath) =>
        this.filesByName.TryGetValue(subpath, out var content)
            ? new InMemoryFile(subpath, content)
            : new NotFoundFileInfo(subpath);

    /// <inheritdoc />
    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    /// <inheritdoc />
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private sealed class InMemoryFile(string name, string content) : IFileInfo
    {
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(content);

        public bool Exists => true;

        public bool IsDirectory => false;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Length => this.bytes.Length;

        public string Name => name;

        public string? PhysicalPath => null;

        public Stream CreateReadStream() => new MemoryStream(this.bytes, writable: false);
    }
}
