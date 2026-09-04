// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Component } from 'react';
import { describe, expect, it } from 'vitest';
import { regionOf } from './caughtRegion';
import { Containment } from './Containment';

describe('regionOf', () => {
    it('names the region the boundary that caught stands around', () => {
        expect(regionOf(new Containment({ region: 'reading_pane', children: null }))).toBe('reading_pane');
    });

    it('reads a failure caught by a boundary this client did not place as the whole application', () => {
        class Elsewhere extends Component {}

        expect(regionOf(new Elsewhere({}))).toBe('application');
    });

    it('reads a failure React named no boundary for as the whole application', () => {
        expect(regionOf(undefined)).toBe('application');
    });
});
