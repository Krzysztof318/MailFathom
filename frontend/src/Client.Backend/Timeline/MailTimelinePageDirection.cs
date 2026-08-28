// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Timeline;

/// <summary>Which way a page continues from the cursor it is asked with.</summary>
/// <remarks>
/// Both directions read the same sorted list rather than two lists: a cursor names a row of that list together with
/// the filters and the order it was read under, so a page collected while scrolling down is what a page read while
/// scrolling back continues from.
/// </remarks>
public enum MailTimelinePageDirection
{
    /// <summary>The page lying after the cursor, which is what a request naming no direction takes.</summary>
    Forward = 0,

    /// <summary>The page lying before the cursor, which the deployment refuses without one.</summary>
    Backward = 1,
}
