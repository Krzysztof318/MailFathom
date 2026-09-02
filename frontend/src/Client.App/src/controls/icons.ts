// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    'arrow_back',
    'attach_file',
    'auto_awesome',
    'calendar_month',
    'check',
    'chevron_right',
    'close',
    'download',
    'expand_more',
    'explore',
    'flag',
    'group',
    'mail',
    'reply',
    'task_alt',
    'topic',
    'warning',
] as const;

export type IconName = (typeof iconNames)[number];

// Material Symbols draws on a 960-unit square whose origin sits at the top of the descender, which is why the box is
// offset rather than starting at zero. It is the upstream viewBox and is the same for every file.
export const glyphBox = '0 -960 960 960';

// The committed files, read at build time rather than at run time: Vite inlines each one as a string, so the outline
// below is a constant in the bundle and no request is made for a symbol. Each file is a single path, which is what the
// upstream export is, so the outline is the one `d` in it.
const outlines: Readonly<Record<string, string | undefined>> = Object.fromEntries(
    Object.entries(import.meta.glob('../assets/icons/*.svg', { query: '?raw', import: 'default', eager: true })).map(
        ([path, source]) => [nameOf(path), /\sd="([^"]+)"/u.exec(source)?.[1]],
    ),
);

/** The name a committed file stands for: what it is called, without its directory and without its extension. */
export function nameOf(path: string): string {
    return path.slice(path.lastIndexOf('/') + 1, -'.svg'.length);
}

/** The outline a symbol is drawn from, or nothing where no file in the tree carries it. */
export function outlineOf(name: string): string | undefined {
    return outlines[name];
}
