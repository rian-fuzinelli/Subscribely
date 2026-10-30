import type { ReactNode } from "react";

/** Moldura das telas de cadastro, login e senha: coluna estreita alinhada à esquerda, sem cartão flutuante. */
export function AuthCard({ title, description, children, footer }: { title: string; description?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="mx-auto w-full max-w-[26rem] px-4 py-12 sm:py-16">
      <h1 className="text-title font-semibold tracking-tight">{title}</h1>
      {description && <p className="mt-1 text-ink-soft">{description}</p>}
      <div className="mt-6 border-t-2 border-ink pt-6">{children}</div>
      {footer && <div className="mt-8 text-meta text-ink-soft">{footer}</div>}
    </div>
  );
}
