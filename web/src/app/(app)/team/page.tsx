import type { Metadata } from "next";
import { InvitationList, InviteForm, UserList, listInvitations, listUsers } from "@/features/team";
import { getUsage } from "@/features/usage";
import { requireAdmin } from "@/shared/auth/session";
import { ButtonLink } from "@/shared/ui/button";
import { Alert, Card, CardTitle, PageHeader } from "@/shared/ui/primitives";

export const metadata: Metadata = { title: "Equipe" };

export default async function TeamPage() {
  const session = await requireAdmin("/team");
  const [users, invitations, usage] = await Promise.all([listUsers(), listInvitations(), getUsage()]);
  const seatsLeft = usage ? usage.seatLimit - usage.seatsUsed : 1;
  return (
    <div className="space-y-8">
      <PageHeader
        title="Equipe"
        description={usage ? usage.seatsUsed + " de " + usage.seatLimit + " assentos ocupados (usuários ativos e convites pendentes)." : undefined}
      />
      <Card className="space-y-4">
        <CardTitle>Convidar pessoa</CardTitle>
        {seatsLeft <= 0 && (
          <Alert
            tone="warning"
            title="Todos os assentos do plano estão ocupados"
            action={<ButtonLink href="/billing/change-plan" size="sm" variant="secondary">Ver planos</ButtonLink>}
          >
            Revogue um convite, desative um usuário ou faça upgrade para convidar mais pessoas.
          </Alert>
        )}
        <InviteForm seatsLeft={seatsLeft} />
      </Card>
      <InvitationList invitations={invitations} />
      <UserList users={users} currentUserId={session.userId} />
    </div>
  );
}
