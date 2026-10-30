import { formatDateTime } from "@/shared/format";
import { ActionButton } from "@/shared/ui/action-button";
import { Badge, Card, CardTitle, EmptyState } from "@/shared/ui/primitives";
import { revokeApiKeyAction } from "../actions";
import type { ApiKey } from "../api";

export function KeyList({ keys }: { keys: ApiKey[] }) {
  return (
    <Card className="space-y-4">
      <CardTitle>Chaves</CardTitle>
      {keys.length === 0 ? (
        <EmptyState title="Nenhuma chave criada" description="Crie uma chave para o seu sistema chamar a API do produto." />
      ) : (
        <ul className="divide-y divide-slate-100">
          {keys.map((key) => (
            <li key={key.id} className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <p className="text-sm font-medium text-slate-900">{key.name}</p>
                <p className="text-xs text-slate-500">
                  <code className="font-mono">{key.prefix}…</code> · criada em {formatDateTime(key.createdAt)}
                </p>
              </div>
              {key.revokedAt ? (
                <Badge>Revogada em {formatDateTime(key.revokedAt)}</Badge>
              ) : (
                <ActionButton
                  size="sm"
                  variant="danger"
                  action={revokeApiKeyAction.bind(null, key.id)}
                  confirm={{
                    title: "Revogar a chave \"" + key.name + "\"?",
                    description: "Os sistemas que usam esta chave param de funcionar imediatamente. Não dá para desfazer.",
                    confirmLabel: "Revogar",
                  }}
                >
                  Revogar
                </ActionButton>
              )}
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
