import { formatDateTime } from "@/shared/format";
import { ActionButton } from "@/shared/ui/action-button";
import { Badge, EmptyState, Section } from "@/shared/ui/primitives";
import { disableUserAction, enableUserAction, revokeInvitationAction } from "../actions";
import type { Invitation, TeamUser } from "../api";

const roleLabel = { TenantAdmin: "Administrador", Member: "Membro" } as const;

export function UserList({ users, currentUserId }: { users: TeamUser[]; currentUserId: string }) {
  return (
    <Section title="Usuários">
      <ul className="divide-y divide-line border-y border-line">
        {users.map((user) => (
          <li key={user.id} className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0">
              <p className="truncate text-meta font-medium text-ink">
                {user.email}
                {user.id === currentUserId && <span className="ml-2 text-meta font-normal text-ink-faint">(você)</span>}
              </p>
              <div className="mt-1 flex gap-2">
                <Badge tone={user.role === "TenantAdmin" ? "brand" : "neutral"}>{roleLabel[user.role]}</Badge>
                {user.status === "Disabled" && <Badge tone="danger">Desativado</Badge>}
              </div>
            </div>
            {user.id !== currentUserId &&
              (user.status === "Active" ? (
                <ActionButton
                  size="sm"
                  variant="danger"
                  action={disableUserAction.bind(null, user.id)}
                  confirm={{
                    title: "Desativar " + user.email + "?",
                    description: "A pessoa perde o acesso na hora e o assento é liberado. Você pode reativar depois.",
                    confirmLabel: "Desativar",
                  }}
                >
                  Desativar
                </ActionButton>
              ) : (
                <ActionButton size="sm" action={enableUserAction.bind(null, user.id)}>
                  Reativar
                </ActionButton>
              ))}
          </li>
        ))}
      </ul>
    </Section>
  );
}

export function InvitationList({ invitations }: { invitations: Invitation[] }) {
  return (
    <Section title="Convites pendentes">
      {invitations.length === 0 ? (
        <EmptyState title="Nenhum convite pendente" />
      ) : (
        <ul className="divide-y divide-line border-y border-line">
          {invitations.map((invitation) => (
            <li key={invitation.id} className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <p className="truncate text-meta font-medium text-ink">{invitation.email}</p>
                <p className="text-meta text-ink-faint">
                  {roleLabel[invitation.role]}, o convite expira em {formatDateTime(invitation.expiresAt)}
                </p>
              </div>
              <ActionButton size="sm" action={revokeInvitationAction.bind(null, invitation.id)}>
                Revogar
              </ActionButton>
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}
