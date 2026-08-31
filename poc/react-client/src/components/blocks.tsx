import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import { answer, factTable, timeline } from "../data";
import { Label } from "./shell";

/**
 * The mockup has result blocks arriving one at a time, each with its own state.
 * Revealing them on a stagger is what makes that visible without a backend.
 */
export function useStaggeredReveal(count: number, step = 320) {
  const [ready, setReady] = useState(0);

  useEffect(() => {
    setReady(0);
    const timers = Array.from({ length: count }, (_, index) =>
      window.setTimeout(() => setReady(index + 1), step * (index + 1)),
    );

    return () => timers.forEach(window.clearTimeout);
  }, [count, step]);

  return ready;
}

function Block({
  name,
  aside,
  accent = false,
  children,
}: {
  name: string;
  aside?: ReactNode;
  accent?: boolean;
  children: ReactNode;
}) {
  return (
    <section
      className={`flex flex-col gap-2.5 rounded-[10px] border border-line bg-surface px-5 py-4 ${
        accent ? "border-l-[3px] border-l-accent" : ""
      }`}
    >
      <div className="flex items-center gap-3">
        <Label>{name}</Label>
        {aside}
      </div>
      {children}
    </section>
  );
}

export function BlockSkeleton() {
  return (
    <div className="flex flex-col gap-2 rounded-[10px] border border-dashed border-line bg-surface/50 px-5 py-4">
      <div className="h-2.5 w-24 animate-pulse rounded bg-hairline" />
      <div className="h-3.5 w-full animate-pulse rounded bg-hairline" />
      <div className="h-3.5 w-4/5 animate-pulse rounded bg-hairline" />
    </div>
  );
}

function Citation({ index, onOpen }: { index: number; onOpen: (cite: number) => void }) {
  return (
    <button
      type="button"
      onClick={() => onOpen(index)}
      aria-label={`Otwórz dowód ${index}`}
      className="mx-0.5 cursor-pointer rounded-[5px] bg-accent-wash px-1.5 py-0.5 align-super text-[10px] font-medium text-accent-deep hover:bg-accent hover:text-white"
    >
      {index}
    </button>
  );
}

export function AnswerBlock({ onOpenCitation }: { onOpenCitation: (cite: number) => void }) {
  return (
    <Block
      accent
      name="Answer"
      aside={
        <>
          <span className="rounded-full bg-ok-wash px-2.5 py-0.5 text-[11px] text-fresh">{answer.confidence}</span>
          <span className="rounded-full bg-warn-wash px-2.5 py-0.5 text-[11px] text-stale">{answer.gap}</span>
          <span className="ml-auto hidden text-xs text-muted sm:inline">{answer.citationCount}</span>
        </>
      }
    >
      <p className="selectable text-[15px] leading-relaxed text-pretty">
        {answer.body.map((segment, index) =>
          "cite" in segment ? (
            <Citation key={index} index={segment.cite} onOpen={onOpenCitation} />
          ) : (
            <span key={index}>{segment.text}</span>
          ),
        )}
      </p>
    </Block>
  );
}

export function TimelineBlock() {
  return (
    <Block name="Timeline" aside={<span className="ml-auto text-xs text-muted">{timeline.summary}</span>}>
      <ol className="grid grid-cols-2 gap-x-4 gap-y-3 lg:grid-cols-4">
        {timeline.events.map((event) => (
          <li
            key={event.date}
            className={`flex flex-col gap-1 border-t-2 pt-3 ${event.current ? "border-accent" : "border-chrome"}`}
          >
            <span className={`font-mono text-xs ${event.current ? "text-accent-deep" : "text-muted"}`}>
              {event.date}
            </span>
            <span className="text-sm font-semibold">{event.title}</span>
            <span className="text-xs text-muted">{event.detail}</span>
          </li>
        ))}
      </ol>
    </Block>
  );
}

export function FactTableBlock({
  onOpenCitation,
  selectedCell,
}: {
  onOpenCitation: (cite: number) => void;
  selectedCell?: number;
}) {
  return (
    <Block name="FactTable" aside={<span className="ml-auto hidden text-xs text-muted lg:inline">{factTable.note}</span>}>
      <div className="-mx-1 overflow-x-auto px-1">
        <table className="w-full min-w-[420px] border-collapse text-left">
          <thead>
            <tr>
              {factTable.columns.map((column) => (
                <th key={column} className="border-b border-line pb-2.5 pr-3 text-xs font-normal text-muted">
                  {column}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {factTable.rows.map((row) => (
              <tr key={row.version} className={row.proposal ? "font-semibold" : ""}>
                <td className="border-b border-hairline py-2.5 pr-3 text-sm">{row.version}</td>
                <td className="border-b border-hairline py-2.5 pr-3 text-sm">{row.price}</td>
                <td className="border-b border-hairline py-2.5 pr-3 text-sm">
                  <span
                    className={
                      selectedCell === row.cite
                        ? "rounded-md border-2 border-accent bg-accent-wash px-2 py-1 font-semibold"
                        : ""
                    }
                  >
                    {row.sla}
                  </span>
                </td>
                <td className="border-b border-hairline py-2.5 pr-3 text-sm">{row.indexation}</td>
                <td className="border-b border-hairline py-2.5 text-sm">
                  <Citation index={row.cite} onOpen={onOpenCitation} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Block>
  );
}
