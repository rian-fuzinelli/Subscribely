import { ButtonLink } from "@/shared/ui/button";

/** 404 genérico: não confirma se o recurso existe em outra conta. */
export default function NotFound() {
  return (
    <main id="conteudo" className="mx-auto flex min-h-dvh max-w-md flex-col items-center justify-center gap-4 px-4 text-center">
      <p className="text-meta font-semibold text-ledger-700">404</p>
      <h1 className="text-title font-semibold text-ink">Página não encontrada</h1>
      <p className="text-meta text-ink-soft">O endereço pode estar errado ou o item não existe mais.</p>
      <ButtonLink href="/overview">Voltar ao portal</ButtonLink>
    </main>
  );
}
