// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Says what the correspondence does for what a block states.</summary>
/// <remarks>
/// The three members are the reason a plan can be honest. Without them the only way to present a fact nothing backs is
/// to present it as though something did, and the only way to present two sources that disagree is to pick one. Both
/// are states a run reaches routinely over years of mail, and both are worse as prose inside an answer than as a value
/// a client can draw differently.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PresentationSupport>))]
public enum PresentationSupport
{
    /// <summary>The correspondence backs what the block states, and the citations are where it does.</summary>
    Supported = 0,

    /// <summary>Nothing found backs what the block states, which the block says rather than leaving the reader to assume.</summary>
    Unsupported = 1,

    /// <summary>The cited sources disagree with each other, and the block presents the disagreement rather than a choice between them.</summary>
    Conflicting = 2,
}
