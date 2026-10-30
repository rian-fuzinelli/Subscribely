import { formatDateTime } from "@/shared/format";
import { ActionButton } from "@/shared/ui/action-button";
import { Badge, EmptyState, Section } from "@/shared/ui/primitives";
import { revokeApiKeyAction } from "../actions";
import type { ApiKey } from "../api";

export function KeyList({ keys }: { keys: ApiKey[] }) {
  return (
    <Section title="Chaves">
      {keys.length === 0 ? (
        <EmptyState title="Nenhuma chave criada" description="Crie uma chave para o seu sistema chamar a API do produto." />
      ) : (
        <ul className="divide-y divide-line border-y border-line">
          {keys.map((key) => (
            <li key={key.id} className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <p className="text-meta font-medium text-ink">{key.name}</p>
                <p className="text-meta text-ink-faint">
                  Começa com <code className="font-mono">{key.prefix}</code>, criada em {formatDateTime(key.createdAt)}
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
    </Section>
  );
}
