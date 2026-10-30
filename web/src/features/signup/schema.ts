import { z } from "zod";

/** Mesmas regras da API (spec 01), para o erro aparecer antes do envio. */
export const signupSchema = z.object({
  companyName: z.string().trim().min(2, "Informe o nome da empresa (mínimo 2 caracteres).").max(100, "Use no máximo 100 caracteres."),
  slug: z
    .string()
    .trim()
    .regex(/^[a-z0-9-]{3,40}$/, "Use de 3 a 40 letras minúsculas, números ou hífen."),
  adminEmail: z.email("Informe um e-mail válido.").max(254),
  password: z.string().min(12, "A senha precisa ter pelo menos 12 caracteres.").max(128, "Use no máximo 128 caracteres."),
  planCode: z.string().min(1, "Escolha um plano."),
  interval: z.enum(["monthly", "annual"], { error: "Escolha mensal ou anual." }),
});

export type SignupInput = z.infer<typeof signupSchema>;

/** Sugestão de identificador a partir do nome da empresa: `Acme & Cia` → `acme-cia`. */
export function suggestSlug(companyName: string): string {
  return companyName
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 40);
}
