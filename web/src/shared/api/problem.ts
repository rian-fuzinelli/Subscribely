/** Erro da API no formato ProblemDetails (RFC 9457), com o `code` estável do Subscribely. */
export type ApiProblem = {
  status: number;
  code: string;
  message: string;
  /** Erros de validação por campo (400). */
  fieldErrors: Record<string, string[]>;
  /** Extensões do ProblemDetails (ex.: `activeSeats`, `targetMaxSeats`). */
  details: Record<string, unknown>;
};

const MESSAGES: Record<string, string> = {
  "slug-taken": "Esse identificador já está em uso. Escolha outro.",
  "email-unavailable": "Esse e-mail não pode ser usado. Use outro endereço.",
  "plan-unavailable": "Esse plano não está mais disponível. Escolha outro plano.",
  "invalid-credentials": "E-mail ou senha incorretos.",
  "too-many-login-attempts": "Muitas tentativas seguidas. Aguarde um minuto e tente de novo.",
  "tenant-unavailable": "Sua conta ainda está sendo preparada ou não está disponível.",
  "invalid-refresh-token": "Sua sessão expirou. Entre de novo.",
  "invalid-reset-token": "Este link de redefinição é inválido ou já expirou. Peça um novo.",
  "invitation-expired": "Este convite expirou ou já foi usado. Peça um novo convite ao administrador.",
  "invitation-exists": "Já existe um convite pendente para esse e-mail.",
  "seat-limit-reached": "O limite de assentos do plano foi atingido.",
  "last-admin": "A conta precisa de pelo menos um administrador ativo.",
  "no-change": "A assinatura já está nesse plano e intervalo.",
  "seats-exceed-target-plan": "Há mais usuários e convites do que o plano escolhido permite.",
  "subscription-not-changeable": "Essa ação não está disponível no status atual da assinatura.",
  "subscription-past-due": "Pague a fatura em aberto antes de fazer upgrade.",
  "subscription-not-found": "A assinatura ainda não foi criada. Tente de novo em instantes.",
  "subscription-inactive": "A assinatura não está ativa. Reative-a para usar o produto.",
  "concurrency-conflict": "A assinatura foi alterada em outra aba ou sessão. Recarregamos os dados; tente de novo.",
  "payment-method-required": "Cadastre um meio de pagamento primeiro.",
  "payment-method-not-found": "Nenhum meio de pagamento cadastrado.",
  "charge-pending": "Já existe uma tentativa de pagamento em processamento para esta fatura.",
  "charges-disabled": "As cobranças estão desligadas neste ambiente.",
  "invoice-not-found": "Fatura não encontrada.",
  "invoice-not-open": "Só faturas em aberto podem ser pagas.",
  "quota-exceeded": "O limite mensal de requisições foi atingido.",
  "project-not-found": "Projeto não encontrado.",
  "cursor-invalid": "Não foi possível carregar essa página da lista.",
  validation: "Revise os campos destacados.",
  forbidden: "Você não tem permissão para essa ação.",
  "not-found": "Não encontrado.",
  unavailable: "Não foi possível falar com o servidor. Verifique a conexão e tente de novo.",
};

const BY_STATUS: Record<number, string> = {
  401: "Sua sessão expirou. Entre de novo.",
  403: MESSAGES.forbidden!,
  404: MESSAGES["not-found"]!,
  429: "Muitas requisições. Aguarde um pouco e tente de novo.",
};

/** Mensagem pt-BR para o usuário final; nunca expõe o detalhe técnico da API. */
export function messageFor(code: string, status: number): string {
  return MESSAGES[code] ?? BY_STATUS[status] ?? "Algo deu errado do nosso lado. Tente de novo em instantes.";
}

const RESERVED = new Set(["type", "title", "status", "detail", "instance", "code", "errors", "traceId"]);

/** Converte a resposta de erro da API (qualquer formato) num {@link ApiProblem}. */
export function toProblem(status: number, body: unknown): ApiProblem {
  const json = body && typeof body === "object" ? (body as Record<string, unknown>) : {};
  const fieldErrors: Record<string, string[]> = {};
  if (json.errors && typeof json.errors === "object") {
    for (const [field, messages] of Object.entries(json.errors as Record<string, unknown>)) {
      if (Array.isArray(messages)) {
        fieldErrors[field] = messages.map(String);
      }
    }
  }

  const code =
    typeof json.code === "string"
      ? json.code
      : Object.keys(fieldErrors).length > 0
        ? "validation"
        : (BY_STATUS[status] ? String(status) : "unknown");
  const details = Object.fromEntries(Object.entries(json).filter(([key]) => !RESERVED.has(key)));

  return { status, code, message: messageFor(code, status), fieldErrors, details };
}

export const unavailableProblem: ApiProblem = {
  status: 503,
  code: "unavailable",
  message: MESSAGES.unavailable!,
  fieldErrors: {},
  details: {},
};
