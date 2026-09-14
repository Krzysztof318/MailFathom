// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The marks the provider grid draws, which are the one family in this client that is neither Material Symbols nor
// MailFathom's own. They are committed under `assets/providers/` and registered in `THIRD_PARTY_LICENSES.md` like every
// other family redistributed inside the bundle, and they are in a directory of their own rather than beside the
// symbols because the two are different works under different terms and the register names the paths.
//
// The design draws each of them from a content delivery network. That is the one place this screen departs from it,
// and the reason is a rule the design does not carry: nothing a screen draws reaches an external origin — a deployment
// on a private network would draw no mark at all, a request per reader would tell a third party who is reading their
// mail and when, and the desktop head serves its document from a scheme where such a request is not reliably
// permitted. So the same marks ship inside the bundle instead of being fetched from one.
//
// **A provider with no mark is the ordinary case rather than a gap.** An operator names their own authorization
// servers and nothing says one of them is a brand anybody drew: what is matched is the name the configuration gives a
// server, and everything else is drawn with the symbol the client already has for a credential.

/** One mark as it was exported, which is a single outline on the square every file in the family is drawn on. */
export interface ProviderMark {
    readonly box: string;
    readonly outline: string;

    /**
     * What to draw the outline in: the brand's own colour, which `styles.css` declares per provider and redeclares
     * where the design gives the dark theme a second one. The family is exported with no colour of its own, so a mark
     * drawn in the current text colour would be nine marks in one grey where the design draws nine brands. A mark
     * committed without a colour declared beside it falls back to that text colour, so the outline is still drawn.
     */
    readonly tint: string;
}

/** The square these are drawn on, which is the family's own and the same for every file in it. */
const markBox = '0 0 24 24';

const marks: Readonly<Record<string, ProviderMark | undefined>> = Object.fromEntries(
    Object.entries(
        import.meta.glob('../assets/providers/*.svg', { query: '?raw', import: 'default', eager: true }),
    ).flatMap(([path, source]) => {
        const outline = typeof source === 'string' ? /\sd="([^"]+)"/u.exec(source)?.[1] : undefined;
        const name = path.slice(path.lastIndexOf('/') + 1, -'.svg'.length);

        return outline === undefined
            ? []
            : [[name, { box: markBox, outline, tint: `var(--color-provider-${name}, currentColor)` }] as const];
    }),
);

/**
 * The mark committed for a provider, or nothing where the client carries none for it.
 *
 * Matched on the name the deployment's configuration gives the server rather than on what it is displayed as, because
 * the name is the key an operator wrote and the display name is prose they may translate. Compared without case for
 * the same reason a configuration key is: `GitHub` and `github` are one name to whoever typed it.
 *
 * @param name The provider's name as the deployment published it.
 * @returns The mark, or `undefined` where nothing in the bundle carries one.
 */
export function providerMark(name: string): ProviderMark | undefined {
    return marks[name.toLowerCase()];
}
