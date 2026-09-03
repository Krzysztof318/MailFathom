// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useLocalization } from '../localization/useLocalization';

// The one file in this client that writes an `iframe`'s `srcdoc`, and the only place a message's own markup is drawn
// as markup. Everywhere else under `src/` the lint rule refuses it outright, and the exception is written into
// `eslint.config.ts` against this path rather than waived at the call site — so a second frame is a configuration diff
// a reviewer meets rather than a line nobody sees. ADR 0024 names this file as that exception.
//
// **Two mechanisms hold two different promises here, and neither substitutes for the other.** The frame is what stops
// the markup running: `sandbox` with neither `allow-scripts` nor `allow-same-origin` denies script, forms, popups,
// navigation, downloads, and an origin of its own. It is *not* what stops the markup reporting — no sandboxing flag
// the HTML Standard defines governs what a framed document may fetch, so a sandboxed frame would load a tracking pixel
// exactly as an unsandboxed one does. What keeps that out is the representation: the service prepares the markup with
// every remote address already removed, unless the reader asked for this one message's pictures. Weakening either half
// is a change to both.
//
// ADR 0024 answered a third question with a second markup surface — the embedded view #1508 builds, which draws each
// open message inline and carries `sandbox="allow-scripts"` so the client's own prepended script can report the frame's
// height. That surface is **this** component with the value it needs handed to it, because the record names one file
// for both rather than one per surface. It takes no such parameter today: nothing draws that surface yet, and a value
// nobody passes is a widening of the one thing on this screen that must not widen by accident.
//
// It draws nothing where there is no markup to draw. A frame with an empty document is a white rectangle that says
// nothing, and what a reader is owed instead is a sentence — which is the surface's to say, because only the surface
// knows why the markup is absent.

export function MessageMarkupFrame({ markup }: { readonly markup: string }) {
    const { translate } = useLocalization();

    if (markup === '') {
        return null;
    }

    return (
        <iframe
            title={translate('fullHtml.frame')}
            sandbox=""
            srcDoc={markup}
            className="min-h-0 w-full flex-1 border-0 bg-white"
        />
    );
}
