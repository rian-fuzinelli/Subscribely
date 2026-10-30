"use client";

import { Menu, X } from "lucide-react";
import { useState } from "react";
import { Button } from "@/shared/ui/button";
import { NavLinks } from "./nav";

/** Menu do celular: abre por cima do conteúdo e fecha ao navegar ou com Esc. */
export function MobileNav({ isAdmin }: { isAdmin: boolean }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="lg:hidden" onKeyDown={(event) => event.key === "Escape" && setOpen(false)}>
      <Button variant="ghost" size="sm" aria-expanded={open} aria-controls="mobile-nav" onClick={() => setOpen((value) => !value)}>
        {open ? <X className="size-5" aria-hidden /> : <Menu className="size-5" aria-hidden />}
        <span className="sr-only">{open ? "Fechar menu" : "Abrir menu"}</span>
      </Button>
      {open && (
        <nav id="mobile-nav" aria-label="Portal" className="absolute inset-x-0 top-14 z-40 border-b border-line bg-paper px-1 py-5">
          <NavLinks isAdmin={isAdmin} onNavigate={() => setOpen(false)} />
        </nav>
      )}
    </div>
  );
}
