# Feature Specification: Portal web do Subscribely

**Feature Branch**: `feat/web-portal` (a criar no kickoff, a partir de `develop`)

**Created**: 2026-10-29

**Status**: Draft

**Input**: User description: "Com a API pronta, preciso criar a interface web que seja conivente com o projeto... Me dê boas opções"

## Contexto

A API cobre todo o ciclo de assinatura: signup, trial, planos, faturas, pagamentos, quota, equipe,
API keys, auditoria e notificações. Hoje só dá para usá-la com ferramentas técnicas (Scalar, curl).
O `OVERVIEW.md` deixou o portal para uma spec própria. Os e-mails já enviados apontam para páginas
que ainda não existem (`/billing`, `/billing/payment-method`, `/billing/invoices/{id}`, `/usage`,
`/invitations/{token}`, `/password-reset?token=`). O portal é a interface humana do produto e precisa
respeitar as mesmas regras de acesso, isolamento entre tenants e linguagem (pt-BR) do restante do projeto.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Assinar um plano e entrar no portal (Priority: P1)

Um visitante vê os planos disponíveis com preço mensal e anual, limites de requisições e de
assentos, escolhe um plano e cria a conta da empresa (nome, identificador, e-mail e senha do
administrador). O portal informa que a conta está sendo preparada e, quando ela fica pronta, o
administrador entra e vê o trial de 14 dias em andamento.

**Why this priority**: sem entrada self-service não há tenant, e todas as outras jornadas dependem dela.

**Independent Test**: um visitante sem conta conclui o cadastro, faz login e vê "Trial — termina em DD/MM/AAAA".

**Acceptance Scenarios**:

1. **Given** um visitante na página de planos, **When** escolhe "Starter mensal" e envia dados válidos, **Then** vê a confirmação de que a conta está sendo preparada e, assim que ela fica pronta, consegue entrar.
2. **Given** um identificador ou e-mail já usado, **When** o visitante envia o cadastro, **Then** o campo é destacado com uma mensagem clara, sem revelar a qual conta o e-mail pertence.
3. **Given** um plano arquivado entre a exibição e o envio, **When** o visitante envia, **Then** vê que o plano não está mais disponível e volta à escolha de planos.

---

### User Story 2 - Acompanhar e mudar a assinatura (Priority: P1)

O administrador do tenant vê o status da assinatura (trial, ativa, em atraso, suspensa, expirada,
cancelada), o plano e o intervalo atuais, o período corrente, uma mudança agendada e um
cancelamento agendado. Ele cadastra ou troca o meio de pagamento, faz upgrade ou downgrade (vendo
antes se a mudança vale agora, com cobrança proporcional, ou no fim do período), cancela, desfaz o
cancelamento e reativa uma assinatura expirada ou cancelada.

**Why this priority**: é o coração do produto: dinheiro e acesso.

**Independent Test**: com uma conta em trial, o administrador cadastra um cartão de teste, faz um upgrade e vê o novo plano; agenda um downgrade e depois o desfaz.

**Acceptance Scenarios**:

1. **Given** uma assinatura ativa no Starter, **When** o administrador escolhe o Pro mensal, **Then** o portal mostra antes de confirmar que a mudança é imediata e gera uma cobrança proporcional; depois de confirmar, mostra o novo plano e um link para a fatura.
2. **Given** uma assinatura ativa no Pro com mais usuários do que o Starter permite, **When** o administrador tenta o downgrade, **Then** vê quantos assentos estão ocupados, qual é o limite do plano alvo e o que fazer (remover usuários ou convites).
3. **Given** um cancelamento agendado, **When** o administrador clica em "Manter assinatura" antes do fim do período, **Then** o cancelamento é desfeito e o aviso some.
4. **Given** uma assinatura suspensa, **When** o administrador entra, **Then** um aviso permanente explica o bloqueio e leva à fatura em aberto e à troca do meio de pagamento.
5. **Given** um Member, **When** abre a assinatura, **Then** vê as informações sem nenhuma ação de alteração.

---

### User Story 3 - Faturas e pagamento (Priority: P2)

O administrador lista as faturas (mais recentes primeiro, com paginação), abre uma fatura com as
linhas (créditos e cobranças), o período e o status, e pede uma nova tentativa de pagamento de uma
fatura em aberto.

**Why this priority**: transparência de cobrança e caminho para sair da inadimplência sem suporte.

**Independent Test**: com uma fatura recusada, o administrador abre a fatura, clica em "Pagar agora" e vê o status mudar para "Paga" quando o pagamento é confirmado.

**Acceptance Scenarios**:

1. **Given** uma fatura de upgrade, **When** o administrador a abre, **Then** vê a linha de crédito (negativa) e a de cobrança, com total igual à soma, em reais (R$ 1.234,56).
2. **Given** uma tentativa de pagamento em andamento, **When** o administrador clica em "Pagar agora" de novo, **Then** vê que já existe uma tentativa em processamento.
3. **Given** o link de fatura recebido por e-mail, **When** o administrador o abre já logado, **Then** cai direto na fatura.

---

### User Story 4 - Uso e quota (Priority: P2)

Administradores e Members veem o uso do mês (requisições usadas / limite, assentos ocupados /
limite), o dia em que o contador zera e o histórico dos últimos 12 meses.

**Why this priority**: evita a surpresa do bloqueio por quota e motiva o upgrade no momento certo.

**Independent Test**: depois de chamadas ao produto, a página de uso mostra o contador e a barra de progresso correspondentes.

**Acceptance Scenarios**:

1. **Given** 85% da quota usada, **When** o usuário abre a página de uso, **Then** vê o alerta de proximidade do limite e um atalho para planos (só administradores).
2. **Given** o histórico de meses anteriores, **When** o usuário abre a página, **Then** vê os 12 últimos meses em ordem.

---

### User Story 5 - Equipe e chaves de API (Priority: P2)

O administrador convida pessoas (Admin ou Member), revoga convites pendentes, desativa e reativa
usuários e gerencia as chaves de API (criar com um nome, ver a chave completa uma única vez,
revogar). O convidado abre o link do convite, define a senha e entra.

**Why this priority**: assentos e API keys são limites cobrados pelo plano e a porta de entrada do produto.

**Independent Test**: o administrador convida um e-mail, o convidado aceita pelo link e aparece na lista; uma chave criada é exibida uma vez e não pode ser vista de novo.

**Acceptance Scenarios**:

1. **Given** o limite de assentos atingido, **When** o administrador tenta convidar, **Then** vê o limite do plano e um atalho para upgrade.
2. **Given** uma chave recém-criada, **When** o administrador fecha o aviso, **Then** a chave completa não é mais exibida em lugar nenhum (só o prefixo).
3. **Given** um convite expirado ou já usado, **When** o convidado abre o link, **Then** vê que o convite não é mais válido e o que fazer (pedir um novo).
4. **Given** o último administrador ativo, **When** alguém tenta desativá-lo, **Then** a ação é recusada com a explicação.

---

### User Story 6 - Acesso à conta (Priority: P3)

Qualquer usuário entra, continua logado enquanto usa o portal, sai e recupera a senha pelo link
de uso único recebido por e-mail.

**Why this priority**: necessário, mas simples e já coberto pela API.

**Independent Test**: um usuário pede a redefinição, abre o link do e-mail, define uma nova senha e entra com ela.

**Acceptance Scenarios**:

1. **Given** credenciais inválidas, **When** o usuário tenta entrar, **Then** vê uma mensagem genérica (sem indicar se o e-mail existe).
2. **Given** muitas tentativas seguidas, **When** o usuário tenta de novo, **Then** vê que precisa aguardar.
3. **Given** uma sessão expirada em segundo plano, **When** o usuário continua navegando, **Then** a sessão é renovada sem perder o que estava fazendo ou, se não for possível, ele volta ao login e depois retorna à página em que estava.

---

### User Story 7 - Trilha de auditoria do tenant (Priority: P3)

O administrador consulta quem fez o quê e quando (controle e produto), com filtros por período e ação.

**Why this priority**: valor de conformidade; uso menos frequente.

**Independent Test**: depois de mudar o plano, o administrador encontra o registro da mudança com o autor e o horário.

**Acceptance Scenarios**:

1. **Given** registros de auditoria, **When** o administrador filtra por período, **Then** vê apenas os registros do período, paginados.

---

### User Story 8 - Playground do produto (Priority: P3)

O administrador usa o produto demo (projetos) direto no portal, com a própria sessão, sem colar uma
chave de API: lista, cria, edita e exclui projetos e vê, a cada chamada, o consumo da quota (limite,
restante e quando zera). Ao atingir o limite, vê o bloqueio e o momento em que o contador zera.

**Why this priority**: torna a quota tangível e demonstra o produto sem ferramentas técnicas; não bloqueia as jornadas de cobrança.

**Independent Test**: o administrador cria um projeto no playground e vê o contador de uso subir em 1; num plano com quota esgotada, a próxima ação é recusada com o horário de liberação.

**Acceptance Scenarios**:

1. **Given** um administrador logado com assinatura ativa, **When** cria um projeto no playground, **Then** o projeto aparece na lista e o indicador de quota mostra 1 requisição a menos.
2. **Given** a quota do mês esgotada, **When** o administrador tenta outra ação, **Then** vê que o limite foi atingido, quando o contador zera e um atalho para upgrade.
3. **Given** uma assinatura suspensa, expirada ou cancelada, **When** o administrador abre o playground, **Then** vê o motivo do bloqueio e a ação de resolução, sem consumir quota.
4. **Given** um Member, **When** abre o portal, **Then** não vê o playground.

---

### Edge Cases

- Conta ainda sendo preparada logo após o cadastro: o portal mostra o progresso, tenta de novo sozinho e só libera o login quando a conta fica pronta; se o preparo falhar, mostra uma mensagem com contato de suporte.
- Duas abas alterando a assinatura ao mesmo tempo: a segunda recebe o aviso de que a assinatura mudou e recarrega os dados antes de tentar de novo.
- O pagamento é confirmado de forma assíncrona: depois de "Pagar agora" ou da troca de cartão, o status da fatura e da assinatura se atualiza sem o usuário recarregar a página.
- Assinatura expirada ou cancelada: login, assinatura, faturas e meio de pagamento continuam acessíveis para o administrador poder reativar; os outros recursos mostram o motivo do bloqueio.
- Usuário de um tenant tentando abrir o endereço de uma fatura de outro tenant: vê "não encontrada", sem confirmar que ela existe.
- Member tentando abrir uma página só de administrador pelo endereço: vê "sem permissão", sem dados.
- Sem conexão ou API indisponível: mensagem clara e opção de tentar de novo, sem perder o formulário preenchido.
- Nome da empresa ou de usuário com HTML ou script: exibido como texto.

## Requirements *(mandatory)*

### Functional Requirements

**Público (sem login)**

- **FR-001**: O portal MUST exibir os planos ativos com preço mensal e anual em reais, quota mensal de requisições e limite de assentos.
- **FR-002**: Visitantes MUST conseguir criar uma conta escolhendo plano e intervalo, com validação de cada campo antes do envio (identificador: 3 a 40 letras minúsculas, números ou hífen; senha: 12 a 128 caracteres).
- **FR-003**: O portal MUST acompanhar o preparo da conta após o cadastro e liberar o login quando ela estiver pronta.
- **FR-004**: O portal MUST servir as páginas de destino dos links enviados por e-mail: aceitar convite (`/invitations/{token}`) e redefinir senha (`/password-reset?token=`).

**Sessão e acesso**

- **FR-005**: Usuários MUST conseguir entrar, sair e recuperar a senha; a sessão MUST ser renovada automaticamente enquanto o usuário usa o portal.
- **FR-006**: O portal MUST mostrar a cada usuário só as ações permitidas ao seu papel (TenantAdmin ou Member) e tratar uma recusa de permissão sem expor dados. Um PlatformAdmin que tentar entrar é orientado a usar as ferramentas administrativas existentes.
- **FR-007**: Credenciais de sessão MUST NOT ficar acessíveis a scripts de terceiros nem em armazenamento que sobreviva ao fechamento da sessão de forma legível por qualquer script da página.
- **FR-008**: O portal MUST levar o usuário de volta à página originalmente pedida depois do login (inclusive a partir dos links dos e-mails).

**Assinatura e pagamento (TenantAdmin; Member só lê)**

- **FR-009**: O portal MUST exibir status, plano, intervalo, período atual, fim do trial, mudança agendada e cancelamento agendado da assinatura.
- **FR-010**: Antes de confirmar uma mudança de plano, o portal MUST informar se ela é imediata (com cobrança proporcional) ou no fim do período, e explicar o bloqueio por assentos com os números (ocupados × limite do plano alvo).
- **FR-011**: Administradores MUST conseguir cancelar, desfazer o cancelamento, desfazer um downgrade agendado e reativar uma assinatura expirada ou cancelada.
- **FR-012**: Administradores MUST conseguir cadastrar ou trocar o meio de pagamento escolhendo um dos cartões de teste do gateway simulado; o portal MUST exibir só bandeira e final do cartão.
- **FR-013**: Status de inadimplência, suspensão, expiração e cancelamento MUST aparecer como um aviso permanente com a ação de resolução.

**Faturas, uso, equipe, chaves, auditoria e playground**

- **FR-014**: Administradores MUST conseguir listar faturas com paginação, abrir o detalhe com as linhas e pedir uma nova tentativa de pagamento de uma fatura em aberto.
- **FR-015**: Administradores e Members MUST ver o uso do mês, os assentos e o histórico de 12 meses; o portal MUST alertar visualmente a partir de 80% da quota.
- **FR-016**: Administradores MUST conseguir convidar, revogar convites, desativar e reativar usuários.
- **FR-017**: Administradores MUST conseguir criar chaves de API (a chave completa aparece uma única vez, com opção de copiar) e revogá-las.
- **FR-018**: Administradores MUST conseguir consultar a auditoria do tenant (controle e produto) com filtros por período e ação.
- **FR-019**: Administradores MUST conseguir usar o produto demo (listar, ver, criar, editar e excluir projetos) pelo portal com a própria sessão, sem informar chave de API, vendo após cada ação o limite, o restante e o momento em que a quota zera.
- **FR-020**: Cada ação do playground MUST passar pelas mesmas regras do uso por chave de API: bloqueio por status da assinatura (sem consumir quota), consumo e bloqueio por quota, alertas de 80% e 100% e registro na auditoria do produto (com o usuário como autor).

**Qualidade transversal**

- **FR-021**: Todo texto MUST estar em português do Brasil; valores em `R$ 1.234,56` e datas e horas no fuso de São Paulo.
- **FR-022**: Mensagens de erro MUST ser compreensíveis para o usuário final e nunca exibir detalhes técnicos internos.
- **FR-023**: O portal MUST funcionar em telas a partir de 360 px de largura e ser operável só pelo teclado.

### Key Entities

- **Plano**: oferta pública com preços mensal e anual, quota de requisições e limite de assentos.
- **Conta (tenant)**: a empresa cliente; tem um identificador único, uma assinatura e uma equipe.
- **Usuário**: pessoa da equipe com papel TenantAdmin ou Member; pode estar ativa ou desativada.
- **Convite**: convite pendente para um e-mail com papel e validade; ocupa um assento.
- **Assinatura**: status, plano, intervalo, período, mudança e cancelamento agendados.
- **Fatura**: número, motivo, status, período, linhas (créditos e cobranças) e total.
- **Meio de pagamento**: bandeira e final do cartão de teste cadastrado.
- **Chave de API**: nome, prefixo visível e data de criação/revogação; o segredo só existe no momento da criação.
- **Uso**: requisições do mês, limite e histórico mensal.
- **Registro de auditoria**: quem (ator), o quê (ação e entidade), quando.
- **Projeto**: item do produto demo (nome, descrição, datas), guardado no banco do tenant.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um visitante sai da página de planos e entra no portal com a conta pronta em menos de 3 minutos.
- **SC-002**: Um administrador conclui uma mudança de plano ou a troca do meio de pagamento em menos de 1 minuto, sem consultar documentação.
- **SC-003**: Em testes com usuários, 90% concluem as jornadas P1 (cadastro e mudança de plano) na primeira tentativa.
- **SC-004**: 95% das páginas exibem o conteúdo principal em até 2 segundos numa conexão doméstica comum.
- **SC-005**: Todas as páginas atendem às diretrizes de acessibilidade WCAG 2.1 nível AA nas verificações automatizadas e manuais.
- **SC-006**: Nenhuma jornada expõe dados de outro tenant ou ações acima do papel do usuário (verificado por testes ponta a ponta com dois tenants e com os papéis TenantAdmin e Member).
- **SC-007**: Todos os links enviados por e-mail levam a uma página funcional do portal.
- **SC-008**: O uso de quota exibido no playground bate exatamente com o da página de uso e com o contador oficial após qualquer sequência de ações.

## Assumptions

- O portal consome os contratos atuais da API sem mudar regras de negócio; ajustes de servidor ficam restritos ao necessário para o navegador acessar a API com segurança (origem permitida, entrega e renovação da sessão).
- Só português do Brasil na v1 (como os e-mails).
- Não há cartão real: o meio de pagamento é a escolha de um dos quatro cartões de teste do gateway simulado (aprovado, recusado, saldo insuficiente, recusado só na primeira tentativa), coerente com o escopo "sem dados de cartão" da spec 05.
- O endereço público do portal é o mesmo configurado para os links dos e-mails, e os caminhos já usados nos e-mails são preservados.
- O portal sobe no ambiente local junto com a API e os demais serviços, com um único comando, como o resto do projeto.
- **Mudança na API (dependência desta spec)**: as rotas de produto passam a aceitar também a sessão de um TenantAdmin, além da chave de API, com as mesmas regras de status, quota, alertas e auditoria. Isso altera a decisão da spec 06 ("`/v1/projects` somente por API key; JWT → 401"); a spec 06 deve ser atualizada no plan para registrar a nova regra e os testes correspondentes.
- O playground é só para TenantAdmin: o papel Member continua somente leitura (tenant, assinatura, uso), como no `OVERVIEW.md`.
- Fora de escopo: **console do PlatformAdmin** (planos, tenants, assinaturas, notificações, auditoria global, anulação de faturas) — o PlatformAdmin segue com as ferramentas administrativas atuais e o console pode virar uma spec futura; app móvel nativo; temas configuráveis; i18n além de pt-BR; cobrança real; edição do perfil do usuário (a API não tem esse endpoint).
