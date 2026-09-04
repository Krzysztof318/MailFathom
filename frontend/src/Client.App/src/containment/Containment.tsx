// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Component, createRef, useEffect, useRef, type ReactNode } from 'react';
import { SecondaryButton } from '../controls/SecondaryButton';
import { useLocalization } from '../localization/useLocalization';
import type { ClientRegion } from '../telemetry/clientTelemetry';

// What a failure nobody expected costs, which without this is the whole client: a component that throws while it
// renders, in a lifecycle, or in an effect unmounts the root it belongs to, and what a single-page application is left
// with is an empty document and a reload — which is a cold start that discards the session, the open thread, and
// whatever was being written. So a region that can fail on its own is surrounded by one of these, and the screen
// around it goes on working while the region says what happened.
//
// This is the client's one class component, and it is a class because React publishes containment nowhere else: there
// is no hook for `getDerivedStateFromError`, and a function component cannot become a boundary. That is a platform
// constraint rather than a second convention, which is why the surface it draws below is an ordinary component.
//
// It reports nothing itself. The root composes what the deployment is told — `main.tsx` passes React's own
// `onCaughtError` — so a second boundary is a second region rather than a second reporter, and what reaches the
// pipeline is decided in one place beside everything else this client says about itself.

interface ContainmentProps {
    /** Which region this stands around, which is what a contained failure is reported under. */
    readonly region: ClientRegion;

    readonly children: ReactNode;
}

interface ContainmentState {
    /** Whether what this contains is failing rather than drawn. */
    readonly failed: boolean;

    /** How many times it has failed, which is what tells a first failure from one that came back. */
    readonly failures: number;
}

export class Containment extends Component<ContainmentProps, ContainmentState> {
    // Where focus goes when a retry succeeds. It wraps what is contained rather than replacing anything the region
    // draws, and `display: contents` is what keeps a wrapper that exists for the keyboard out of the layout.
    private readonly recovered = createRef<HTMLDivElement>();

    override state: ContainmentState = { failed: false, failures: 0 };

    static getDerivedStateFromError(): Pick<ContainmentState, 'failed'> {
        return { failed: true };
    }

    // Counted here rather than in the derivation above, which is handed the error and not the state before it. A
    // failure that repeats is worth saying out loud: a region that quietly draws the same sentence after every retry
    // is a person pressing a control that appears to do nothing.
    override componentDidCatch(): void {
        this.setState((before) => ({ failures: before.failures + 1 }));
    }

    override componentDidUpdate(_: ContainmentProps, before: ContainmentState): void {
        if (before.failed && !this.state.failed) {
            this.recovered.current?.focus();
        }
    }

    private readonly retry = (): void => {
        this.setState({ failed: false });
    };

    override render(): ReactNode {
        if (this.state.failed) {
            return <FailedRegion again={this.state.failures > 1} onRetry={this.retry} />;
        }

        return (
            <div className="contents" ref={this.recovered} tabIndex={-1}>
                {this.props.children}
            </div>
        );
    }
}

/**
 * What a region draws instead of itself, which is the error state every surface owes: what failed, and a way out that
 * is not a reload.
 *
 * Retrying is offered however many times it has failed, because a region nobody can leave is the state § _UX_ refuses
 * and the frame around this one is not always somewhere else to go — what a second failure changes is the sentence,
 * so that pressing it again is a decision rather than a loop.
 */
function FailedRegion({ again, onRetry }: { readonly again: boolean; readonly onRetry: () => void }) {
    const { translate } = useLocalization();
    const surface = useRef<HTMLDivElement>(null);

    // This stands where somebody was reading, which is a view change like any other: focus left on a control inside
    // the region that has just gone is focus on nothing at all. The alert says it to a screen reader; this is what
    // puts everybody else at the start of it.
    useEffect(() => {
        surface.current?.focus();
    }, []);

    return (
        <div className="flex flex-col items-start gap-2 px-5.5 py-4" ref={surface} role="alert" tabIndex={-1}>
            <p className="text-sm text-warning">
                {translate(again ? 'containment.failedAgain' : 'containment.failed')}
            </p>

            <SecondaryButton label={translate('connection.retry')} onActivate={onRetry} />
        </div>
    );
}
