// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The one global every artboard's files share. A data file adds its sample data under `data`, and an artboard's logic
// registers under `artboards` a factory the design runtime calls with its own `DCLogic` and `React` once both exist.
// Logic an artboard splits into modules of its own hangs each one here under its own name.
window.MailFathomDesign = { data: {}, artboards: {} };
