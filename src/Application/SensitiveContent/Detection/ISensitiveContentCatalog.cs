// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>Declares everything one scanner can look for, so configuration naming it can be judged before it runs.</summary>
/// <remarks>
/// <para>
/// This is the seam the two switches are validated through. The configuration binder drops a list item it cannot bind
/// and carries on, so a mistyped category name would otherwise leave that category off while an operator reads their own
/// file as proof that it is on — the quiet failure this whole feature exists to prevent. Startup matches every
/// configured name against a catalog and refuses to start when one matches nothing.
/// </para>
/// <para>
/// A catalog says what a scanner knows, never what a deployment asked for. It is therefore free of configuration and can
/// be read before any of it is resolved, which is what lets validation, defaulting, and canonical spelling all come from
/// one place.
/// </para>
/// <para>
/// A scanner registers a catalog of its own. Nothing here registers one, so a switch turned on in a deployment carrying
/// no scanner fails at startup rather than running unprotected.
/// </para>
/// </remarks>
public interface ISensitiveContentCatalog
{
    /// <summary>Gets which of the two switches this catalog belongs to.</summary>
    SensitiveContentScannerKind Scanner { get; }

    /// <summary>Gets every category the scanner can look for, with the rules inside each.</summary>
    IReadOnlyList<SensitiveContentCategoryDefinition> Categories { get; }
}
