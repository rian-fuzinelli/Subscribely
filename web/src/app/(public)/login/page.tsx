import type { Metadata } from "next";
import Link from "next/link";
import { LoginForm } from "@/features/auth";
import { Alert } from "@/shared/ui/primitives";
import { AuthCard } from "../auth-card";

export const metadata: Metadata = { title: "Entrar" };

type Search = { next?: string; email?: string; expired?: string; reset?: string; invited?: string; loggedOut?: string; reason?: string };

const notices: { key: keyof Search; tone: "brand" | "success" | "warning"; text: string }[] = [
  { key: "expired", tone: "warning", text: "Sua sessão expirou. Entre de novo para continuar de onde parou." },
  { key: "reset", tone: "success", text: "Senha redefinida. Entre com a nova senha." },
  { key: "invited", tone: "success", text: "Convite aceito! Entre com o seu e-mail e a senha que você criou." },
  { key: "loggedOut", tone: "brand", text: "Você saiu da sua conta." },
];

export default async function LoginPage({ searchParams }: { searchParams: Promise<Search> }) {
  const search = await searchParams;
  const notice = notices.find((n) => search[n.key]);
  return (
    <AuthCard
      title="Entrar"
      description="Acesse o portal da sua empresa."
      footer={
        <>
          Ainda não tem conta?{" "}
          <Link href="/signup" className="font-medium text-ledger-700 hover:underline">
            Comece o teste grátis
          </Link>
        </>
      }
    >
      <div className="space-y-5">
        {notice && <Alert tone={notice.tone}>{notice.text}</Alert>}
        {search.reason === "platform-admin" && (
          <Alert tone="warning">O portal é para as contas clientes. Administradores da plataforma usam as ferramentas administrativas.</Alert>
        )}
        <LoginForm next={search.next} email={search.email} />
      </div>
    </AuthCard>
  );
}
