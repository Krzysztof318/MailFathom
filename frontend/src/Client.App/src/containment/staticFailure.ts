// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The surface for the two failures a boundary cannot contain: one thrown while the last-resort boundary itself is
// drawn, which unmounts the root and leaves an empty document, and one raised before there is a root to render into at
// all. Neither leaves any of the application running, so what stands there is markup `index.html` already carries
// rather than anything this bundle renders — a component would need the tree that has just failed, and a sentence
// assembled here would need the catalogue that arrives with it.
//
// It is the one surface in this client that is not read in both languages, and that is what carrying it in the
// document costs: what says it is the document, whose language is its own, and the alternative would be to render the
// catalogue out of a bundle whose rendering is what failed.

/** The template `index.html` carries, put in front of somebody in place of whatever is left of the client. */
export function showStaticFailure(): void {
    const carried = document.getElementById('client-failed');

    if (!(carried instanceof HTMLTemplateElement)) {
        return;
    }

    const shown = carried.content.cloneNode(true);
    const root = document.getElementById('root');

    // Appended where there is no root, because a document that carries none is exactly the failure this stands for.
    if (root === null) {
        document.body.append(shown);
    } else {
        root.replaceChildren(shown);
    }
}
