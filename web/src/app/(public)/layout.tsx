import Link from "next/link";
import type { ReactNode } from "react";
import { getSession } from "@/shared/auth/session";
import { ButtonLink } from "@/shared/ui/button";
import { Logo } from "@/shared/ui/logo";

export default async function PublicLayout({ children }: { children: ReactNode }) {
  const session = await getSession();
  return (
    <div className="flex min-h-dvh flex-col">
      <header className="border-b border-slate-200 bg-white">
        <nav aria-label="Principal" className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4">
          <Logo />
          <div className="flex items-center gap-2">
            <Link href="/#planos" className="hidden rounded-lg px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100 sm:block">
              Planos
            </Link>
            {session ? (
              <ButtonLink href="/overview" size="sm">
                Ir para o portal
              </ButtonLink>
            ) : (
              <>
                <ButtonLink href="/login" variant="ghost" size="sm">
                  Entrar
                </ButtonLink>
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
      <footer className="border-t border-slate-200 bg-white">
        <p className="mx-auto max-w-6xl px-4 py-6 text-xs text-slate-500">
          Subscribely · ambiente de demonstração: os pagamentos usam cartões de teste e nenhuma cobrança é real.
        </p>
      </footer>
    </div>
  );
}
