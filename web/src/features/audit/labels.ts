const verbs: Record<string, string> = { created: "criou", updated: "alterou", deleted: "excluiu" };

const entities: Record<string, string> = {
  subscription: "assinatura",
  invoice: "fatura",
  invoice_line: "item de fatura",
  plan: "plano",
  plan_version: "versão de plano",
  user: "usuário",
  invitation: "convite",
  api_key: "chave de API",
  tenant: "conta",
  payment_method: "meio de pagamento",
  charge: "cobrança",
  dunning: "retentativa de cobrança",
  project: "projeto",
};

const actors: Record<string, string> = { User: "Usuário", ApiKey: "Chave de API", System: "Sistema", Webhook: "Gateway", Anonymous: "Anônimo" };

/** `subscription.updated` → `alterou assinatura`. */
export function describeAction(action: string): string {
  const [entity = "", verb = ""] = action.split(".");
  return (verbs[verb] ?? verb) + " " + (entities[entity] ?? entity.replaceAll("_", " "));
}


export function actorLabel(actorType: string): string {
  return actors[actorType] ?? actorType;
}
