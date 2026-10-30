/** Esqueleto enquanto a página busca os dados (sem saltos de layout). */
export default function Loading() {
  return (
    <div role="status" aria-label="Carregando" className="animate-pulse space-y-6">
      <div className="h-8 w-48 rounded-control bg-line" />
      <div className="h-40 rounded-surface bg-line" />
      <div className="grid gap-4 md:grid-cols-2">
        <div className="h-32 rounded-surface bg-line" />
        <div className="h-32 rounded-surface bg-line" />
      </div>
    </div>
  );
}
