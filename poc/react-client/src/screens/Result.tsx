import { evidenceDocument, evidenceList, run, suggestedAction } from "../data";
import { AnswerBlock, BlockSkeleton, FactTableBlock, TimelineBlock, useStaggeredReveal } from "../components/blocks";
import { Card, Chip, Label, PaneHeader } from "../components/shell";

function EvidencePanel({ onOpenCitation }: { onOpenCitation: (cite: number) => void }) {
  return (
    <>
      <div className="flex flex-col gap-1 border-b border-line px-5 py-3.5">
        <Label>EvidenceList</Label>
        <span className="text-sm font-semibold">{evidenceList.summary}</span>
      </div>

      <div className="flex flex-col gap-2.5 px-5 py-3.5 lg:min-h-0 lg:flex-1 lg:overflow-y-auto">
        {evidenceList.items.map((item) => (
          <button
            key={item.cite}
            type="button"
            onClick={() => onOpenCitation(item.cite)}
            className="flex cursor-pointer flex-col gap-1.5 rounded-[10px] border border-line bg-surface px-4 py-3 text-left transition-colors hover:border-accent"
          >
            <span className="flex items-center gap-2.5">
              <span className="rounded-[5px] bg-accent-deep px-1.5 py-0.5 font-mono text-[10px] text-white">
                {item.cite}
              </span>
              <span className="text-sm font-semibold">{item.title}</span>
            </span>
            <span className="text-[13px] leading-snug text-ink-soft">{item.quote}</span>
            <span className="font-mono text-[11px] text-faint">{item.meta}</span>
          </button>
        ))}
      </div>

      <div className="flex flex-col gap-2 border-t border-line bg-surface px-5 py-3.5">
        <Label>SuggestedAction</Label>
        <p className="text-sm leading-snug">{suggestedAction.question}</p>
        <p className="text-xs text-muted">{suggestedAction.reason}</p>
        <div className="flex gap-2.5 pt-1">
          <button type="button" className="cursor-pointer rounded-lg bg-accent px-4 py-2 text-[13px] text-white">
            {suggestedAction.confirm}
          </button>
          <button
            type="button"
            className="cursor-pointer rounded-lg border border-chrome px-4 py-2 text-[13px] text-ink-soft"
          >
            {suggestedAction.dismiss}
          </button>
        </div>
      </div>
    </>
  );
}

function EvidenceInspector({ onOpenInMail }: { onOpenInMail: () => void }) {
  return (
    <>
      {/* On a phone the pane header already names the document, so only the actions repeat. */}
      <div className="flex shrink-0 flex-col gap-2.5 border-b border-line bg-surface px-5 py-3">
        <div className="hidden items-center gap-3 lg:flex">
          <span className="label shrink-0">{evidenceDocument.position}</span>
          <span className="min-w-0 truncate text-sm font-semibold">{evidenceDocument.title}</span>
        </div>
        <div className="flex gap-2.5">
          <button
            type="button"
            onClick={onOpenInMail}
            className="cursor-pointer rounded-lg bg-accent px-3.5 py-1.5 text-[13px] text-white"
          >
            Otwórz w poczcie
          </button>
          <button type="button" className="cursor-pointer rounded-lg border border-chrome px-3.5 py-1.5 text-[13px] text-ink-soft">
            Pobierz
          </button>
        </div>
      </div>

      <div className="flex shrink-0 flex-wrap gap-x-6 gap-y-1 border-b border-line px-5 py-3 font-mono text-[11px] text-muted">
        {evidenceDocument.meta.map((item) => (
          <span key={item}>{item}</span>
        ))}
      </div>

      <div className="selectable flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-5">
        <p className="text-[13px] leading-relaxed text-faint">{evidenceDocument.before}</p>
        <p className="border-l-4 border-mark-edge bg-mark px-5 py-4 text-[15px] leading-relaxed text-pretty">
          {evidenceDocument.highlight}
        </p>
        <p className="text-[13px] leading-relaxed text-faint">{evidenceDocument.after}</p>

        <Card className="mt-auto flex flex-col gap-2 px-5 py-3.5 xl:flex-row xl:items-center xl:gap-4">
          <p className="text-[13px] leading-snug text-ink-soft">
            Fragment podpiera:{" "}
            {evidenceDocument.supports.map((support, index) => (
              <span key={support}>
                {index > 0 && " · "}
                <b>{support}</b>
              </span>
            ))}
            {evidenceDocument.supportsTail}
          </p>
          <button type="button" className="ml-auto shrink-0 cursor-pointer text-[13px] text-accent-deep">
            Dodaj do Sprawy
          </button>
        </Card>
      </div>
    </>
  );
}

function DecisionTrail() {
  return (
    <div className="flex flex-col gap-2 rounded-[10px] border border-line bg-rail px-5 py-4">
      <Label>Ślad decyzji</Label>
      <ol className="flex flex-col gap-0.5 text-[13px] leading-relaxed text-ink-soft">
        {evidenceDocument.trail.map((step) => (
          <li key={step}>{step}</li>
        ))}
      </ol>
    </div>
  );
}

export function Result({
  cite,
  isWide,
  onOpenCitation,
  onCloseCitation,
  onOpenInMail,
  onBack,
}: {
  cite?: number;
  isWide: boolean;
  onOpenCitation: (cite: number) => void;
  onCloseCitation: () => void;
  onOpenInMail: () => void;
  onBack: () => void;
}) {
  const ready = useStaggeredReveal(3);

  // On a phone the inspector is a screen of its own, reached and left with back.
  if (cite !== undefined && !isWide) {
    return (
      <div className="flex min-h-0 flex-1 flex-col bg-sunken">
        <PaneHeader title={evidenceDocument.title} onBack={onCloseCitation} />
        <EvidenceInspector onOpenInMail={onOpenInMail} />
      </div>
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="flex shrink-0 flex-col gap-2.5 border-b border-line bg-surface px-4 py-3 lg:px-8">
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={onBack}
            aria-label="Wstecz"
            className="-ml-1 cursor-pointer text-lg leading-none text-accent-deep lg:hidden"
          >
            ‹
          </button>
          <span className="shrink-0 rounded-md bg-accent px-2 py-1 font-mono text-[10px] tracking-widest text-white">
            AI
          </span>
          <h1 className="min-w-0 flex-1 text-[15px] font-medium">{run.question}</h1>
          <button type="button" className="hidden cursor-pointer text-[13px] text-muted xl:block">
            Zapisz jako Sprawę
          </button>
          <button type="button" className="hidden cursor-pointer text-[13px] text-muted xl:block">
            Odśwież
          </button>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {run.scope.map((chip) => (
            <Chip key={chip}>{chip}</Chip>
          ))}
          <span className="ml-auto hidden font-mono text-[11px] text-faint xl:inline">{run.meta}</span>
        </div>
      </header>

      {/* One scroll on a phone, two independent panes once the layout is side by side. */}
      <div className="flex min-h-0 flex-1 flex-col overflow-y-auto lg:flex-row lg:overflow-hidden">
        <div className="flex flex-col gap-3 p-4 lg:min-h-0 lg:flex-1 lg:overflow-y-auto lg:p-5">
          {ready >= 1 ? <AnswerBlock onOpenCitation={onOpenCitation} /> : <BlockSkeleton />}
          {ready >= 2 ? <TimelineBlock /> : <BlockSkeleton />}
          {ready >= 3 ? <FactTableBlock onOpenCitation={onOpenCitation} selectedCell={cite} /> : <BlockSkeleton />}
          {cite !== undefined && <DecisionTrail />}
        </div>

        <aside
          className={`flex shrink-0 flex-col border-t border-line lg:min-h-0 lg:w-[380px] lg:border-t-0 lg:border-l ${
            cite === undefined ? "bg-rail" : "bg-sunken"
          }`}
        >
          {cite === undefined ? (
            <EvidencePanel onOpenCitation={onOpenCitation} />
          ) : (
            <>
              <button
                type="button"
                onClick={onCloseCitation}
                className="shrink-0 cursor-pointer border-b border-line bg-surface px-5 py-2 text-left text-xs text-muted hover:text-ink"
              >
                ‹ Wróć do listy dowodów
              </button>
              <EvidenceInspector onOpenInMail={onOpenInMail} />
            </>
          )}
        </aside>
      </div>
    </div>
  );
}
