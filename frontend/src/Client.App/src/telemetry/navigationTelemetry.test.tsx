// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { act, render } from '@testing-library/react';
import { noTelemetry, TelemetryContext, type ClientTelemetry } from './clientTelemetry';
import { useNavigationTelemetry } from './navigationTelemetry';

function movesRecorded(): { readonly telemetry: ClientTelemetry; readonly moves: [string, number][] } {
    const moves: [string, number][] = [];

    return {
        telemetry: {
            ...noTelemetry,
            navigated: (space, askedAt) => moves.push([space, askedAt]),
        },
        moves,
    };
}

function Screen({ space }: { readonly space: string | null }) {
    useNavigationTelemetry(space);

    return <p>{space ?? 'nothing yet'}</p>;
}

function asking(): void {
    act(() => {
        window.dispatchEvent(new HashChangeEvent('hashchange'));
    });
}

describe('useNavigationTelemetry', () => {
    it('reports the move a person asked for, naming the space they reached', () => {
        const { telemetry, moves } = movesRecorded();
        const { rerender } = render(
            <TelemetryContext value={telemetry}>
                <Screen space="discover" />
            </TelemetryContext>,
        );

        asking();
        rerender(
            <TelemetryContext value={telemetry}>
                <Screen space="mail" />
            </TelemetryContext>,
        );

        expect(moves.map(([space]) => space)).toEqual(['mail']);
        expect(moves[0]?.[1]).toBeGreaterThan(0);
    });

    it('reports nothing for the space a run opens on, which nobody moved to', () => {
        const { telemetry, moves } = movesRecorded();

        render(
            <TelemetryContext value={telemetry}>
                <Screen space="discover" />
            </TelemetryContext>,
        );

        expect(moves).toEqual([]);
    });

    it('reports nothing while the deployment has not said which spaces there are', () => {
        const { telemetry, moves } = movesRecorded();
        const { rerender } = render(
            <TelemetryContext value={telemetry}>
                <Screen space={null} />
            </TelemetryContext>,
        );

        asking();
        rerender(
            <TelemetryContext value={telemetry}>
                <Screen space={null} />
            </TelemetryContext>,
        );

        expect(moves).toEqual([]);
    });

    it('reports one move per address change rather than one per render', () => {
        const { telemetry, moves } = movesRecorded();
        const { rerender } = render(
            <TelemetryContext value={telemetry}>
                <Screen space="discover" />
            </TelemetryContext>,
        );

        asking();
        rerender(
            <TelemetryContext value={telemetry}>
                <Screen space="mail" />
            </TelemetryContext>,
        );
        rerender(
            <TelemetryContext value={telemetry}>
                <Screen space="discover" />
            </TelemetryContext>,
        );

        expect(moves.map(([space]) => space)).toEqual(['mail']);
    });

    it('stops listening once the screen is gone', () => {
        const { telemetry, moves } = movesRecorded();
        const { unmount } = render(
            <TelemetryContext value={telemetry}>
                <Screen space="discover" />
            </TelemetryContext>,
        );

        unmount();
        asking();

        expect(moves).toEqual([]);
    });
});
