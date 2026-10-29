# Plan — 04 Invoicing

## Branch and delivery

- **Branch:** `feat/billing-invoicing` (base: `develop`)
- **PR slicing:** PR único.

## Technical approach

Agregado `Invoice` imutável no módulo Billing, criado por uma `InvoiceFactory` com um método por
motivo. Proração isolada em `ProrationCalculator` (função pura). O `PeriodEndJob` e o `ChangePlan`
da spec 03 chamam a factory na mesma transação da transição de assinatura. `MarkInvoicePaid` é
exposto via `Contracts` para o módulo Payments.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Domínio | `src/Modules/Subscribely.Modules.Billing/Domain/Invoices/{Invoice.cs, InvoiceLine.cs, InvoiceFactory.cs, ProrationCalculator.cs}` | Regras |
| Endpoints | `.../Features/Invoices/{ListInvoices, GetInvoice, VoidInvoice}/*` | Slices |
| Integração | `.../Features/Subscriptions/{ChangePlan, Jobs/PeriodEndJob}` | Emitir faturas |
| Persistência | `.../Persistence/Configurations/{InvoiceConfiguration, InvoiceLineConfiguration}.cs`, sequence `invoice_number_seq` | |
| Contracts | `.../Contracts/IBillingApi.cs` | `GetInvoice`, `MarkInvoicePaid` |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Erro de arredondamento | Testes de propriedade: soma das linhas = total; crédito ≤ preço antigo |
| Fatura duplicada | UNIQUE + tratamento de `23505` |

## Rollback and reversibility

- Migration aditiva (tabelas + sequence). Reverter o PR basta.

## Testing strategy

- Unit (100%): `ProrationCalculator` (tabela de casos: início, meio, fim do período; mensal→anual), `InvoiceFactory`.
- Integration: renovação via job; upgrade gera fatura; paginação por cursor; void com RBAC.
