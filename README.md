# Subscribely

Sistema de **assinaturas e cobrança** para empresas que vendem software por assinatura (SaaS): quem paga,
quanto, quando e o que pode usar.

Cada cliente é uma empresa que cria a conta, escolhe um plano e convida a equipe. A partir daí o Subscribely
cuida do resto:

- **Teste grátis** de 14 dias, sem cartão, com aviso por e-mail três dias antes do fim.
- **Cobrança automática** no fim do teste e a cada renovação (mensal ou anual).
- **Troca de plano com conta exata:** o upgrade vale na hora e cobra só a diferença proporcional; o downgrade fica para o fim do período.
- **Inadimplência tratada:** novas tentativas em 1, 3 e 7 dias, suspensão do acesso e cancelamento após 30 dias — sem nunca apagar os dados do cliente.
- **Limite de uso** de requisições e de usuários por plano, com alertas em 80% e 100%.
- **E-mails** de convite, senha, faturas e alertas, e uma **trilha de auditoria** de tudo o que muda.

## Como é construído

- API em .NET 10: monolito modular, com um banco de dados exclusivo por cliente.
- Infraestrutura: PostgreSQL, Redis para contar o uso, Hangfire para as tarefas agendadas e um gateway de pagamento simulado que manda confirmações assinadas, como um gateway real faria.
- Portal web em Next.js, onde o cliente vê a assinatura, as faturas e o uso.
- Testes: mais de 300 automatizados cobrindo esses fluxos de ponta a ponta.

## Rodando localmente

Você só precisa do [Docker Desktop](https://www.docker.com/products/docker-desktop/).

```bash
git clone https://github.com/rian-fuzinelli/Subscribely.git
cd Subscribely
cp .env.example .env
docker compose up -d --build
```

A primeira subida leva alguns minutos (compila a API e o portal). Depois, abra **http://localhost:3000**.

| Endereço | O que é |
|---|---|
| http://localhost:3000 | Portal web (cadastro, assinatura, faturas, uso) |
| http://localhost:8025 | Caixa de e-mails de teste (Mailpit): convites, faturas, alertas |
| http://localhost:8080/scalar | Documentação interativa da API |
| http://localhost:8080/health/ready | Saúde da API |

Para parar: `docker compose down` (os dados ficam) ou `docker compose down -v` (apaga tudo).

> Os valores do `.env.example` servem para rodar na sua máquina. Para qualquer outro ambiente, troque as senhas e a `JWT_SIGNING_KEY`.

## Testando em 5 minutos

1. Em http://localhost:3000, clique em **Criar conta**, escolha um plano e preencha os dados. O portal mostra a conta sendo preparada e libera o login em segundos.
2. Em **Assinatura → Meio de pagamento**, escolha um cartão de teste. Nenhum dado de cartão real é usado:

   | Cartão de teste | O que acontece nas cobranças |
   |---|---|
   | Visa final 4242 | Aprovadas |
   | Visa final 0002 | Recusadas (cartão recusado) |
   | Mastercard final 9995 | Recusadas (saldo insuficiente) |
   | Mastercard final 3184 | Recusa a primeira tentativa e aprova as seguintes |

3. Em **Mudar de plano**, veja a prévia do que acontece (agora com cobrança proporcional, ou no fim do período) antes de confirmar.
4. No **Playground**, crie projetos no produto de demonstração e veja a quota do mês diminuir a cada ação.
5. Em **Equipe**, convide alguém e abra o e-mail do convite no Mailpit (http://localhost:8025).

O fim do teste grátis e as renovações acontecem pelas tarefas agendadas, conforme o calendário real. Para ver
o ciclo inteiro em segundos (teste → fatura → falha → novas tentativas → suspensão → reativação), rode os testes
de ponta a ponta abaixo: eles controlam o relógio.

## Rodando os testes

Requer o [.NET 10 SDK](https://dotnet.microsoft.com/download), o Docker em execução e, para o portal, o [Node.js 22+](https://nodejs.org/).

```bash
dotnet test                                   # unitários, arquitetura e integração (inclui os cenários de ponta a ponta)
cd web && npm ci && npm test && npm run build # portal
```

Os testes de integração sobem PostgreSQL, Redis e Mailpit em containers automaticamente.

## Estrutura

```
src/
  Subscribely.Api/      API: composição dos módulos, autenticação, health checks
  BuildingBlocks/       código compartilhado (dinheiro, resultados, banco, outbox, jobs, auditoria)
  Modules/              Tenancy, Billing, Payments, Metering, Notifications, Audit, Projects
web/                    portal em Next.js (detalhes em web/README.md)
tests/                  testes unitários, de arquitetura e de integração
specs/                  especificação de cada módulo, escrita antes do código
docs/                   decisões de arquitetura
```

Quer entender o porquê das escolhas técnicas? Veja as [decisões de arquitetura](docs/architecture.md) e a
[visão geral das specs](specs/OVERVIEW.md).
