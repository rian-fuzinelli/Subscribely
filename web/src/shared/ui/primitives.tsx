import { CircleAlert, CircleCheck, Info, TriangleAlert } from "lucide-react";
import type { ComponentProps, ReactNode } from "react";
import { cn } from "@/shared/cn";

/** Superfície de papel: borda fina, sem sombra. */
export function Card({ className, ...props }: ComponentProps<"section">) {
  return <section className={cn("rounded-surface border border-line bg-paper p-5 sm:p-6", className)} {...props} />;
}

export function CardTitle({ className, ...props }: ComponentProps<"h2">) {
  return <h2 className={cn("text-body font-semibold text-ink", className)} {...props} />;
}

export type Tone = "neutral" | "brand" | "success" | "warning" | "danger";

const markers: Record<Tone, string> = {
  neutral: "bg-ink-faint",
  brand: "bg-ledger-600",
  success: "bg-ledger-600",
  warning: "bg-due-600",
  danger: "bg-fault-600",
};

const inks: Record<Tone, string> = {
  neutral: "text-ink-soft",
  brand: "text-ledger-700",
  success: "text-ledger-700",
  warning: "text-due-700",
  danger: "text-fault-700",
};

/** Status como texto com marcador quadrado (sem pílula): lê como um carimbo de extrato. */
export function Badge({ tone = "neutral", className, children, ...props }: ComponentProps<"span"> & { tone?: Tone }) {
  return (
    <span className={cn("inline-flex shrink-0 items-center gap-1.5 text-meta font-medium whitespace-nowrap", inks[tone], className)} {...props}>
      <span aria-hidden className={cn("size-2 rounded-[1px]", markers[tone])} />
      {children}
    </span>
  );
}

const alertSurfaces: Record<Tone, string> = {
  neutral: "border-ink-faint bg-paper",
  brand: "border-ledger-600 bg-ledger-50",
  success: "border-ledger-600 bg-ledger-50",
  warning: "border-due-600 bg-due-50",
  danger: "border-fault-600 bg-fault-50",
};

const alertIcons = { neutral: Info, brand: Info, success: CircleCheck, warning: TriangleAlert, danger: CircleAlert };

/** Aviso com régua lateral na cor do significado. `danger`/`warning` são anunciados pelo leitor de tela. */
export function Alert({
  tone = "brand",
  title,
  children,
  action,
  className,
}: {
  tone?: Tone;
  title?: string;
  children?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  const Icon = alertIcons[tone];
  return (
    <div
      role={tone === "danger" || tone === "warning" ? "alert" : "status"}
      className={cn("flex flex-col gap-3 border-l-[3px] py-3 pr-4 pl-4 sm:flex-row sm:items-center", alertSurfaces[tone], className)}
    >
      <Icon className={cn("hidden size-4 shrink-0 sm:block", inks[tone])} aria-hidden />
      <div className="flex-1 text-meta text-ink">
        {title && <p className="font-semibold">{title}</p>}
        {children && <div className={cn("text-ink-soft", title && "mt-0.5")}>{children}</div>}
      </div>
      {action}
    </div>
  );
}

export function PageHeader({ title, description, actions }: { title: string; description?: ReactNode; actions?: ReactNode }) {
  return (
    <header className="flex flex-col gap-4 border-b border-line pb-5 sm:flex-row sm:items-end sm:justify-between">
      <div className="max-w-2xl">
        <h1 className="text-title font-semibold tracking-tight text-ink">{title}</h1>
        {description && <p className="mt-1 text-ink-soft">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </header>
  );
}

/** Estado vazio: diz o que falta e qual é o próximo passo. */
export function EmptyState({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="border-y border-dashed border-line-strong py-8">
      <p className="font-medium text-ink">{title}</p>
      {description && <p className="mt-1 max-w-prose text-meta text-ink-soft">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

/** Medidor com valor acessível; vira âmbar em 80% e vermelho no limite. */
export function Meter({ label, value, max }: { label: string; value: number; max: number }) {
  const percent = max > 0 ? Math.min(100, Math.round((value / max) * 100)) : 0;
  const color = percent >= 100 ? "bg-fault-600" : percent >= 80 ? "bg-due-600" : "bg-ledger-600";
  return (
    <div
      role="meter"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={max}
      aria-valuenow={Math.min(value, max)}
      aria-valuetext={percent + "%"}
      className="h-1.5 w-full overflow-hidden rounded-[1px] bg-line"
    >
      <div className={cn("h-full transition-[width] duration-500", color)} style={{ width: percent + "%" }} />
    </div>
  );
}

export function DescriptionList({ items, className }: { items: { term: string; value: ReactNode }[]; className?: string }) {
  return (
    <dl className={cn("grid gap-x-8 gap-y-4 sm:grid-cols-2", className)}>
      {items.map((item) => (
        <div key={item.term}>
          <dt className="text-meta text-ink-faint">{item.term}</dt>
          <dd className="mt-0.5 text-ink">{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}

/** Título de seção dentro de uma página (sem caixa alta nem sobrancelha). */
export function SectionTitle({ className, ...props }: ComponentProps<"h2">) {
  return <h2 className={cn("text-body font-semibold text-ink", className)} {...props} />;
}

/** Grupo de informação da página: título e conteúdo, sem caixa em volta (o papel é reservado aos documentos). */
export function Section({ title, description, children, className }: { title: string; description?: ReactNode; children: ReactNode; className?: string }) {
  const id = "secao-" + title.toLowerCase().normalize("NFD").replace(/[^a-z0-9]+/g, "-");
  return (
    <section aria-labelledby={id} className={cn("space-y-3", className)}>
      <div>
        <SectionTitle id={id}>{title}</SectionTitle>
        {description && <p className="mt-0.5 text-meta text-ink-soft">{description}</p>}
      </div>
      {children}
    </section>
  );
}
