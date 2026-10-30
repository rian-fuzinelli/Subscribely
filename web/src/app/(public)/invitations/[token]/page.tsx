import type { Metadata } from "next";
import { NewPasswordForm } from "@/features/auth";
import { AuthCard } from "../../auth-card";

export const metadata: Metadata = { title: "Aceitar convite" };

/** Destino do link do e-mail de convite (`/invitations/{token}`). */
export default async function InvitationPage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  return (
    <AuthCard title="Você recebeu um convite" description="Crie uma senha para entrar na conta da sua equipe.">
      <NewPasswordForm token={decodeURIComponent(token)} kind="invitation" />
    </AuthCard>
  );
}
