// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The set of symbols the client has, and the outlines they are drawn from. It sits beside `Icon.tsx` rather than in it
// because a module exporting a component exports nothing else, which is what keeps a development reload replacing a
// component instead of reloading the page.
//
// The design project settles the iconography as Material Symbols Rounded at weight 300 with `FILL 0` and `GRAD 0`,
// addressed by the ligature name Google's catalogue uses, and every glyph is that file committed under `assets/icons/`
// rather than a font fetched from a CDN: a deployment that keeps mail on its own server does not hand
// `fonts.gstatic.com` a request per reader, and the desktop head has to render with no route out at all.
//
// Only the symbols the client actually draws are in the tree, which is what `iconNames` is for — it is the list, the
// type, and what the suite checks the directory against, so a name that has no file fails a test rather than rendering
// an empty square, and a file nothing draws fails the same test rather than sitting in the bundle unnoticed.

/** Every symbol the client draws, by the name Google's catalogue gives it. */
export const iconNames = [
    'add_a_photo',
    'all_inbox',
    'archive',
    'arrow_back',
    'arrow_right',
    'attach_file',
    'auto_awesome',
    'calendar_month',
    'cancel',
    'check',
    'chevron_left',
    'chevron_right',
    'close',
    'code',
    'dark_mode',
    'delete',
    'description',
    'download',
    'draft',
    'drive_file_move',
    'edit_square',
    'expand_more',
    'explore',
    'flag',
    'folder',
    'forward',
    'group',
    'handshake',
    'inbox',
    'info',
    'label_important',
    'language',
    'lock',
    'logout',
    'mail',
    'mark_email_unread',
    'menu',
    'outbox',
    'pending_actions',
    'person',
    'reply',
    'reply_all',
    'report',
    'schedule',
    'send',
    'settings',
    'tab',
    'task_alt',
    'topic',
    'warning',
] as const;

export type IconName = (typeof iconNames)[number];

/** One symbol as it was exported: the coordinate system its outline was drawn in, and the outline. */
export interface Glyph {
    readonly box: string;
    readonly outline: string;
}

// The committed files, read at build time rather than at run time: Vite inlines each one as a string, so a symbol is a
// constant in the bundle and no request is made for one. Each file is a single path, which is what the upstream export
// is, so the outline is the one `d` in it.
//
// **The box is read from the file rather than assumed**, and that is not defensiveness. Most of the upstream
// `wght300_24px` exports are drawn on a 960-unit square whose origin sits at the top of the descender —
// `viewBox="0 -960 960 960"` — but not all of them are: `auto_awesome` at the commit the register names carries no
// `viewBox` at all and is drawn on the 24-unit box its `width` and `height` declare. A fixed box would render that
// glyph as a speck in a corner, silently, and the same would happen to the next file upstream exports the older way.
const symbols: Readonly<Record<string, Glyph | undefined>> = Object.fromEntries(
    Object.entries(import.meta.glob('../assets/icons/*.svg', { query: '?raw', import: 'default', eager: true })).map(
        ([path, source]) => [nameOf(path), glyphIn(source)],
    ),
);

/** The name a committed file stands for: what it is called, without its directory and without its extension. */
export function nameOf(path: string): string {
    return path.slice(path.lastIndexOf('/') + 1, -'.svg'.length);
}

/** The symbol a committed file holds, or nothing where the file is not one path in a box this can read. */
export function glyphIn(source: string): Glyph | undefined {
    const outline = /\sd="([^"]+)"/u.exec(source)?.[1];

    if (outline === undefined) {
        return undefined;
    }

    const declared = /\sviewBox="([^"]+)"/u.exec(source)?.[1];

    if (declared !== undefined) {
        return { box: declared, outline };
    }

    // An export with no `viewBox` is drawn on the square its own attributes declare, which is what the SVG default
    // user space is. Both are always present in these files, so a file carrying neither is one this cannot read.
    const width = /\swidth="(\d+)"/u.exec(source)?.[1];
    const height = /\sheight="(\d+)"/u.exec(source)?.[1];

    return width === undefined || height === undefined ? undefined : { box: `0 0 ${width} ${height}`, outline };
}

/** The symbol drawn under a name, or nothing where no file in the tree carries it. */
export function glyphOf(name: string): Glyph | undefined {
    return symbols[name];
}
