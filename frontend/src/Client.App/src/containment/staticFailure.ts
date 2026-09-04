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

    // The client declares the language it is being read in on the document itself, and what is about to stand there
    // reads in one language whatever that said — so the declaration is put back with it, rather than leaving a screen
    // reader to pronounce English under whichever locale the session was in.
    document.documentElement.lang = 'en';

    const shown = carried.content.cloneNode(true);

    // Read out of the fragment before it is inserted, because appending a fragment empties it: what is put in the
    // document is this element, and a reference taken afterwards would have nothing left to take.
    const surface = shown instanceof DocumentFragment ? shown.firstElementChild : null;
    const root = document.getElementById('root');

    // Appended where there is no root, because a document that carries none is exactly the failure this stands for.
    if (root === null) {
        document.body.append(shown);
    } else {
        root.replaceChildren(shown);
    }

    // This replaces everything that was on the screen, which is the largest view change the client can make, so focus
    // goes to it for the reason § _UX_ places focus on any other: whatever held it a moment ago is no longer in the
    // document. The alert says it to a screen reader; this is what puts everybody else at the start of it.
    //
    // Taken after the caller's own work rather than during it. This is called from inside React's handling of a
    // failure it could not contain, and React puts focus back where it was before that work when it is done — measured
    // in the built bundle, where focus placed here synchronously, or in a microtask, is on the document body a moment
    // later and only a task waited out survives it. Nothing else in this client has to wait: no other surface is put
    // in front of somebody from inside React's own commit.
    if (surface instanceof HTMLElement) {
        setTimeout(() => {
            surface.focus();
        });
    }
}
