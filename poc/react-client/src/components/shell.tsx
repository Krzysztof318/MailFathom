import type { ReactNode } from "react";
import type { Section } from "../navigation";

/** The mockup draws its navigation with glyphs rather than an icon set. */
const sections = [
  { id: "discover", glyph: "◎", name: "Odkrywaj" },
  { id: "mail", glyph: "✉", name: "Poczta" },
  { id: "cases", glyph: "◫", name: "Sprawy" },
] as const satisfies readonly { id: Section; glyph: string; name: string }[];

type NavProps = { active: Section; onSelect: (section: Section) => void };

export function Rail({ active, onSelect }: NavProps) {
  return (
    <nav
      aria-label="Główna nawigacja"
      className="hidden w-[84px] shrink-0 flex-col items-center gap-6 border-r border-line bg-rail py-5 lg:flex"
    >
      <div className="flex size-9 items-center justify-center rounded-[11px] bg-accent text-base font-semibold text-white">
        M
      </div>

      {sections.map((section) => {
        const selected = section.id === active;
        return (
          <button
            key={section.id}
            type="button"
            aria-current={selected ? "page" : undefined}
            onClick={() => onSelect(section.id)}
            className={`flex w-full cursor-pointer flex-col items-center gap-1 py-2 transition-colors ${
              selected
                ? "border-r-[3px] border-accent bg-accent-wash text-accent-deep"
                : "text-muted hover:text-ink"
            }`}
          >
            <span className="text-lg leading-none">{section.glyph}</span>
            <span className={`text-[11px] ${selected ? "font-semibold" : ""}`}>{section.name}</span>
          </button>
        );
      })}

      <div className="mt-auto flex size-8 items-center justify-center rounded-full bg-chrome text-[11px] text-ink-soft">
        KK
      </div>
    </nav>
  );
}

export function TabBar({ active, onSelect }: NavProps) {
  return (
    <nav
      aria-label="Główna nawigacja"
      className="flex shrink-0 border-t border-line bg-rail pb-[env(safe-area-inset-bottom)] lg:hidden"
    >
      {sections.map((section) => {
        const selected = section.id === active;
        return (
          <button
            key={section.id}
            type="button"
            aria-current={selected ? "page" : undefined}
            onClick={() => onSelect(section.id)}
            className={`flex flex-1 cursor-pointer flex-col items-center gap-0.5 py-2 ${
              selected ? "text-accent-deep" : "text-muted"
            }`}
          >
            <span className="text-lg leading-none">{section.glyph}</span>
            <span className={`text-[11px] ${selected ? "font-semibold" : ""}`}>{section.name}</span>
          </button>
        );
      })}
    </nav>
  );
}

export function Label({ children }: { children: ReactNode }) {
  return <span className="label">{children}</span>;
}

export function Chip({
  children,
  tone = "quiet",
}: {
  children: ReactNode;
  tone?: "quiet" | "accent";
}) {
  const tones = {
    quiet: "bg-rail border-line text-ink",
    accent: "bg-accent-wash border-accent-wash text-accent-dark",
  };
  return (
    <span className={`rounded-full border px-3 py-1 text-xs whitespace-nowrap ${tones[tone]}`}>{children}</span>
  );
}

export function Card({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <div className={`rounded-[10px] border border-line bg-surface ${className}`}>{children}</div>;
}

/** A phone-only header that makes the pushed pane feel like a screen with a back affordance. */
export function PaneHeader({
  title,
  onBack,
  action,
}: {
  title: string;
  onBack: () => void;
  action?: ReactNode;
}) {
  return (
    <header className="flex shrink-0 items-center gap-2 border-b border-line bg-surface px-3 py-2.5 lg:hidden">
      <button
        type="button"
        onClick={onBack}
        aria-label="Wstecz"
        className="-ml-1 cursor-pointer rounded-lg px-2 py-1 text-lg leading-none text-accent-deep active:bg-accent-wash"
      >
        ‹
      </button>
      <h2 className="min-w-0 flex-1 truncate text-sm font-semibold">{title}</h2>
      {action}
    </header>
  );
}
