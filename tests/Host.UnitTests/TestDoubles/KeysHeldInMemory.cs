// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Where the framework's data protection keys go while a host is composed or started in a test.</summary>
/// <remarks>
/// Nothing in these tests protects anything, so the repository is never meaningfully read from; it exists so the key
/// manager settles on something that is not a directory in somebody's home. A unit test may not write to the file
/// system, and a build agent is the wrong place for the default to be exercised either.
/// </remarks>
internal sealed class KeysHeldInMemory : IXmlRepository
{
    private readonly List<XElement> elements = [];

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements() => this.elements.AsReadOnly();

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName) => this.elements.Add(element);
}
