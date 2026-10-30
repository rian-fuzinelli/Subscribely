/** Cartões de teste do gateway simulado (spec 05): nenhum dado de cartão real passa pelo portal. */
export const testCards = [
  { token: "pm_card_ok", label: "Visa final 4242", outcome: "Pagamentos aprovados" },
  { token: "pm_card_declined", label: "Visa final 0002", outcome: "Pagamentos recusados (cartão recusado)" },
  { token: "pm_card_insufficient_funds", label: "Mastercard final 9995", outcome: "Pagamentos recusados (saldo insuficiente)" },
  { token: "pm_card_fail_then_ok", label: "Mastercard final 3184", outcome: "Recusa a primeira tentativa e aprova as seguintes" },
] as const;

export type TestCardToken = (typeof testCards)[number]["token"];

export function isTestCard(token: string): token is TestCardToken {
  return testCards.some((card) => card.token === token);
}
