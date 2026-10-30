import Link from "next/link";
import type { ReactNode } from "react";
import { getSession } from "@/shared/auth/session";
import { ButtonLink } from "@/shared/ui/button";
import { Logo } from "@/shared/ui/logo";

export default async function PublicLayout({ children }: { children: ReactNode }) {
  const session = await getSession();
  return (
    <div className="flex min-h-dvh flex-col">
      <header className="border-b border-line bg-paper">
        <nav aria-label="Principal" className="mx-auto flex h-14 max-w-6xl items-center justify-between px-4">
          <Logo />
          <div className="flex items-center gap-1 sm:gap-2">
            <Link href="/#planos" className="hidden rounded-control px-3 py-2 text-meta font-medium text-ink-soft hover:text-ink sm:block">
              Planos
            </Link>
            {session ? (
              <ButtonLink href="/overview" size="sm">
                Abrir o portal
              </ButtonLink>
            ) : (
              <>
                <Link href="/login" className="rounded-control px-3 py-2 text-meta font-medium text-ink-soft hover:text-ink">
                  Entrar
                </Link>
                <ButtonLink href="/signup" size="sm">
                  Criar conta
                </ButtonLink>
              </>
            )}
          </div>
        </nav>
      </header>
      <main id="conteudo" className="flex-1">
        {children}
      </main>
      <footer className="border-t border-line">
        <p className="mx-auto max-w-6xl px-4 py-5 text-meta text-ink-faint">
          Ambiente de demonstração: os pagamentos usam cartões de teste e nenhuma cobrança é real.
        </p>
      </footer>
    </div>
  );
}
