import { formatDateTime } from "@/shared/format";
import { ActionButton } from "@/shared/ui/action-button";
import { Badge, Card, CardTitle, EmptyState } from "@/shared/ui/primitives";
import { disableUserAction, enableUserAction, revokeInvitationAction } from "../actions";
import type { Invitation, TeamUser } from "../api";

const roleLabel = { TenantAdmin: "Administrador", Member: "Membro" } as const;

export function UserList({ users, currentUserId }: { users: TeamUser[]; currentUserId: string }) {
  return (
    <Card className="space-y-4">
      <CardTitle>Usuários</CardTitle>
      <ul className="divide-y divide-slate-100">
        {users.map((user) => (
          <li key={user.id} className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-slate-900">
                {user.email}
                {user.id === currentUserId && <span className="ml-2 text-xs font-normal text-slate-500">(você)</span>}
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
    </Card>
  );
}

export function InvitationList({ invitations }: { invitations: Invitation[] }) {
  return (
    <Card className="space-y-4">
      <CardTitle>Convites pendentes</CardTitle>
      {invitations.length === 0 ? (
        <EmptyState title="Nenhum convite pendente" />
      ) : (
        <ul className="divide-y divide-slate-100">
          {invitations.map((invitation) => (
            <li key={invitation.id} className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-slate-900">{invitation.email}</p>
                <p className="text-xs text-slate-500">
                  {roleLabel[invitation.role]} · expira em {formatDateTime(invitation.expiresAt)}
                </p>
              </div>
              <ActionButton size="sm" action={revokeInvitationAction.bind(null, invitation.id)}>
                Revogar
              </ActionButton>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
