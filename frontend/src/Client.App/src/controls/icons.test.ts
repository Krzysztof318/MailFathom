// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { iconNames, nameOf, outlineOf } from './icons';

describe('icons', () => {
    it('draws every symbol the client declares from a file committed in the tree', () => {
        const missing = iconNames.filter((name) => outlineOf(name) === undefined);

        expect(missing).toEqual([]);
    });

    it('carries no symbol the client does not draw, so nothing sits in the bundle unnoticed', () => {
        const committed = Object.keys(
            import.meta.glob('../assets/icons/*.svg', { query: '?raw', import: 'default', eager: true }),
        ).map(nameOf);

        expect([...committed].sort()).toEqual([...iconNames].sort());
    });
});
