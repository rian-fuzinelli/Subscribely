import { LogOut } from "lucide-react";
import type { ReactNode } from "react";
import { logoutAction } from "@/features/auth";
import { SubscriptionBanner, getSubscription, getTenant } from "@/features/subscription";
import { requireSession } from "@/shared/auth/session";
import { Button } from "@/shared/ui/button";
import { Logo } from "@/shared/ui/logo";
import { MobileNav } from "./mobile-nav";
import { NavLinks } from "./nav";

export default async function AppLayout({ children }: { children: ReactNode }) {
  const session = await requireSession("/overview");
  const isAdmin = session.role === "TenantAdmin";
  const [tenant, subscription] = await Promise.all([getTenant(), getSubscription()]);

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[16rem_1fr]">
      <aside className="hidden border-r border-slate-200 bg-white lg:flex lg:flex-col">
        <div className="flex h-16 items-center px-5">
          <Logo href="/overview" />
        </div>
        <nav aria-label="Portal" className="flex-1 px-3 py-4">
          <NavLinks isAdmin={isAdmin} />
        </nav>
      </aside>

      <div className="flex min-w-0 flex-col">
        <header className="relative flex h-16 items-center justify-between gap-4 border-b border-slate-200 bg-white px-4 sm:px-6">
          <div className="flex min-w-0 items-center gap-3">
            <MobileNav isAdmin={isAdmin} />
            <span className="lg:hidden">
              <Logo href="/overview" />
            </span>
            <p className="hidden truncate text-sm font-semibold text-slate-900 lg:block">{tenant?.name ?? "Sua conta"}</p>
          </div>
          <div className="flex items-center gap-3">
            <div className="hidden text-right sm:block">
              <p className="max-w-56 truncate text-sm text-slate-900">{session.email ?? "Usuário"}</p>
              <p className="text-xs text-slate-500">{isAdmin ? "Administrador" : "Membro"}</p>
            </div>
            <form action={logoutAction}>
              <Button type="submit" variant="ghost" size="sm">
                <LogOut className="size-4" aria-hidden />
                <span>Sair</span>
              </Button>
            </form>
          </div>
        </header>

        <main id="conteudo" className="mx-auto w-full max-w-6xl flex-1 space-y-6 px-4 py-8 sm:px-6">
          <SubscriptionBanner subscription={subscription} isAdmin={isAdmin} />
          {children}
        </main>
      </div>
    </div>
  );
}
