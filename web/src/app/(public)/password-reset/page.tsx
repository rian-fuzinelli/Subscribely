import type { Metadata } from "next";
import Link from "next/link";
import { NewPasswordForm } from "@/features/auth";
import { Alert } from "@/shared/ui/primitives";
import { AuthCard } from "../auth-card";

export const metadata: Metadata = { title: "Redefinir senha" };

/** Destino do link do e-mail de redefinição (`/password-reset?token=`). */
export default async function PasswordResetPage({ searchParams }: { searchParams: Promise<{ token?: string }> }) {
  const { token } = await searchParams;
  return (
    <AuthCard title="Redefinir senha" description="Escolha uma nova senha para a sua conta.">
      {token ? (
        <NewPasswordForm token={token} kind="reset" />
      ) : (
        <Alert tone="warning" title="Link incompleto">
          Abra o link exatamente como chegou no e-mail ou{" "}
          <Link href="/forgot-password" className="font-medium underline">
            peça um novo
          </Link>
          .
        </Alert>
      )}
    </AuthCard>
  );
}
