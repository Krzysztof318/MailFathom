// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { useSpace } from './useSpace';

// The address bar is the one copy of which space is being shown, and correcting an address nobody answers is the one
// write this hook makes to it. What is asserted here is that the correction leaves the entry otherwise as it found it:
// the shell marks the entry showing with how many steps the back gesture has to unwind, and an address rewritten with
// nothing in its place would spend one of those presses on an entry that no longer says what it is for.

const stepsTaken = 'mailfathom.back';

beforeEach(() => {
    window.history.replaceState(null, '', '/');
});

describe('useSpace', () => {
    it('answers with the space the address names, where it names one this credential is offered', () => {
        window.history.replaceState(null, '', '#/mail');

        const { result } = renderHook(() => useSpace(['discover', 'mail']));

        expect(result.current).toBe('mail');
    });

    it('writes the space it fell back to into an address that named none', () => {
        const { result } = renderHook(() => useSpace(['discover', 'mail']));

        expect(result.current).toBe('discover');
        expect(window.location.hash).toBe('#/discover');
    });

    it('carries the shell’s own mark across the address it corrects', () => {
        window.history.replaceState({ [stepsTaken]: 2 }, '', '#/nowhere');

        renderHook(() => useSpace(['discover', 'mail']));

        expect(window.location.hash).toBe('#/discover');
        expect(window.history.state).toStrictEqual({ [stepsTaken]: 2 });
    });

    it('answers with nothing where the grant carries no space at all, and writes no address', () => {
        const { result } = renderHook(() => useSpace([]));

        expect(result.current).toBeNull();
        expect(window.location.hash).toBe('');
    });
});
