# Secco.Intranet — Architecture Decision Records (ADR)

> Fonte da verdade arquitetural do produto secco-intranet.
> Nenhum código deve contradizer uma ADR com status **Aceita**.
> Para mudar uma decisão, cria-se uma nova ADR que **substitui** a anterior — ADRs nunca são editadas retroativamente nem apagadas.
> Decisões da plataforma (multi-tenancy, `Result<T>`, nomenclatura de banco...) estão em `secco-platform/docs/adr/secco-platform-adrs.md` — este documento cobre apenas decisões específicas deste produto.

**Última atualização:** 2026-09-02 (ADR-0006 e ADR-0007 adicionadas)

---

## Como usar este documento

1. Toda decisão arquitetural relevante (difícil de reverter, ou que afete a extensibilidade do projeto por terceiros) vira uma ADR.
2. Status possíveis: `Proposta` → `Aceita` → `Substituída por ADR-YYYY` ou `Rejeitada`.
3. Ao iniciar qualquer trabalho neste repositório, consultar as ADRs aplicáveis antes de codificar.
4. ADRs curtas são melhores que ADRs completas. Contexto, decisão, consequências. Nada mais.

### Template

```markdown
## ADR-XXXX: Título da decisão

**Status:** Proposta | Aceita | Substituída por ADR-YYYY | Rejeitada
**Data:** AAAA-MM-DD

### Contexto
Qual problema estamos resolvendo e quais forças estão em jogo.

### Decisão
O que foi decidido, em voz ativa: "Usaremos X porque Y."

### Consequências
O que fica mais fácil, o que fica mais difícil, o que passa a ser proibido.
```

---

## ADR-0001: Setor = Role tenant-scoped do Secco.SecureGate

**Status:** Aceita
**Data:** 2026-07-19

### Contexto
A Intranet organiza documentos, processos e outros recursos por Setor/Departamento
(ex: Financeiro, RH, Infraestrutura). Era preciso decidir como representar "usuário
pertence a este setor, com este nível de acesso (admin/user)". O Secco.SecureGate já
resolve autorização multi-tenant através do conceito de Role (`(tenant, role) →
permissões`, com claims no padrão `recurso:ação`, conforme ADR-0021 da plataforma) —
criar um conceito paralelo de "perfil de setor" duplicaria essa infraestrutura.

### Decisão
Cada Setor cadastrado cria automaticamente duas Roles tenant-scoped no SecureGate:
`{slug}-admin` e `{slug}-user`. Vincular um usuário a um setor é vincular ele a uma
dessas Roles via `UserRole` — não existe tabela própria `Usuario↔Setor` na Intranet.
Um usuário com acesso a múltiplos setores recebe múltiplas Roles. A geração das claims
(`intranet:{slug}:{recurso}:{ação}`) para os dois perfis padrão **não é automatizada**
— fica a critério de quem adota o sistema; só será automatizada se um caso de uso
concreto justificar o esforço.

### Consequências
- Nenhuma tabela nova de autorização na Intranet — reaproveita 100% o que o SecureGate
  já resolve (login, claims, resolução de permissão).
- Um "Gerente" que precise acessar vários setores simplesmente acumula Roles, sem
  necessidade de um conceito de "perfil transversal" separado.
- A flag `Fixo` existe na entidade `Setor` para o adotante marcar um setor que não pode
  ser desativado nem excluído pela tela de administração. É genérica — não amarra
  nenhum recurso a um setor específico (revisto em 2026-09-12: o recurso de Inventário,
  cogitado como "dono nato" da Infraestrutura na primeira redação desta ADR, **não**
  pertence a setor nenhum; ver Consequências revisadas abaixo).
- Fica proibido criar qualquer relação direta de autorização fora do modelo de Role do
  SecureGate (ex: uma flag `IsAdminDoSetor` numa tabela de usuário local) — isso
  fragmentaria a fonte de verdade de autorização.
- Nem todo recurso pertence a um setor. O Inventário é o primeiro caso: sua Role
  tenant-scoped não segue o padrão `{slug}-admin`/`{slug}-user` (não há slug de setor
  para derivar) — é uma Role própria (ex.: `inventario-admin`), atribuível a qualquer
  usuário do tenant, tipicamente alguém de Infraestrutura, mas sem essa exigência. O
  desenho exato (nome da Role, claims) fica para a spec do Inventário.

---

## ADR-0002: Monolito — Secco.Intranet.Web consome a Application layer diretamente

**Status:** Aceita
**Data:** 2026-07-19

### Contexto
Era preciso decidir se o front-end (MVC) chamaria a Application layer em processo, ou
via uma Api HTTP separada (`Secco.Intranet.Api`). Um cenário futuro plausível é migrar
parte ou todo o front para Angular, React ou Blazor WebAssembly, o que favoreceria uma
Api separada desde já — mas a preferência declarada é por um monolito simples de
manter, com uma equipe pequena.

### Decisão
`Secco.Intranet.Web` (MVC) consome a Application layer diretamente (via Mediator/
handlers), sem uma Api HTTP separada por enquanto. Para manter a porta de saída aberta
para uma futura extração, três regras são obrigatórias: (1) Controllers nunca acessam
`DbContext`/repositório diretamente — sempre via handler; (2) Views recebem
ViewModel/DTO, nunca entidade de domínio; (3) nenhuma regra de negócio mora em
Controller ou View.

### Consequências
- Menos complexidade operacional agora (um artefato, sem HTTP interno, sem contrato de
  API pra versionar).
- Seguindo as três regras acima, extrair uma Api de verdade no futuro (para Angular/
  React/Blazor WASM) é um trabalho mecânico de expor os mesmos handlers atrás de
  endpoints — não uma reescrita de regra de negócio.
- Fica proibido qualquer atalho onde a View ou o Controller acessem o EF Core
  diretamente "só dessa vez" — isso quebra a premissa que barateia a extração futura.

---

## ADR-0003: Sistema de temas via Razor Class Library (RCL)

**Status:** Aceita
**Data:** 2026-07-19

### Contexto
O projeto é open source e precisa permitir customização visual profunda por quem
adotar (o autor se descreve como iniciante em design, mas com base de Bootstrap). O
mecanismo precisa: (a) não prender o projeto a um único framework CSS, para não
excluir contribuidores que prefiram outra stack; (b) permitir múltiplos temas de saída
mantidos pelo próprio projeto; (c) suportar modo claro/escuro.

### Decisão
Cada tema é um pacote Razor Class Library (RCL) independente, com sua própria pasta
`wwwroot` (CSS/JS) e `Views`. O core (`Secco.Intranet.Web`) resolve o tema ativo via
`IViewLocationExpander` e não impõe framework CSS — só carrega o que o tema declara. O
projeto disponibiliza dois temas de saída, ambos em Bootstrap customizado via
variáveis Sass: `Secco.Intranet.Themes.Vertical` (menu lateral, padrão) e
`Secco.Intranet.Themes.Horizontal` (menu no topo). Todo tema publicado — inclusive de
terceiros — deve suportar `data-bs-theme="light"` e `data-bs-theme="dark"`.

### Consequências
- Contribuidores externos podem publicar temas em qualquer stack CSS sem tocar no
  core.
- Trocar de tema é configuração, não deploy de código novo do core.
- Cada tema novo tem um custo fixo: declarar as duas variantes de cor (claro/escuro)
  não é automático — cores saturadas geralmente precisam de ajuste manual de tom entre
  as variantes para manter contraste legível.
- Fica proibido um tema "esquecer" o modo escuro — é parte do contrato mínimo de
  qualquer tema aceito no projeto.

---

## ADR-0004: Contrato de tema em projeto próprio (`Secco.Intranet.Web.Theming`)

**Status:** Aceita
**Data:** 2026-08-31

### Contexto
A ADR-0003 decidiu que cada tema é uma RCL independente, resolvida por
`IViewLocationExpander`. Ao implementar o primeiro tema apareceu um ciclo: o core precisa
referenciar a RCL do tema (para publicar os assets), e o tema precisa dos tipos que os
parciais recebem como `@model`. Se esses tipos morarem em `Secco.Intranet.Web`, tema e core
passam a se referenciar mutuamente. As alternativas eram `@model dynamic` nos parciais —
que joga fora a checagem em tempo de compilação exatamente na fronteira que terceiros vão
implementar — ou duplicar os modelos em cada tema.

### Decisão
Os contratos ficam em um projeto próprio, `Secco.Intranet.Web.Theming`, que não referencia
nada do produto: `ThemeOptions`, `ThemeViewLocationExpander`, `SetorHue` e os modelos dos
parciais. Core e temas referenciam esse projeto; o core referencia também as RCLs dos temas
que quer publicar. Todo tema entrega, sob `Themes/{Nome}/Views/Shared/`: `_Layout`,
`_PageHeader`, `_Card`, `_Badge`, `_EmptyState`, `_Pagination` e o markup dos view components
`Navigation` e `UserMenu` — cuja lógica permanece no core.

A regra que sustenta o contrato: **o core decide o que a página mostra, o tema decide como
ela parece.** Views de página do core não escrevem markup de sidebar, card ou badge à mão;
elas compõem os parciais do contrato.

### Consequências
- Publicar um tema de terceiro exige referenciar um projeto pequeno e estável, não a Web
  inteira — é o que torna a promessa da ADR-0003 praticável.
- Trocar a aparência de todo o produto não exige tocar em nenhuma view de página.
- Fica proibido a uma view de página do core emitir markup estrutural próprio: quem faz isso
  cria uma página que um tema novo não consegue reestilizar, e o contrato deixa de valer.
- Cada parcial novo do contrato é uma quebra para temas existentes; acrescentar um exige
  atualizar os temas de saída junto.

---

## ADR-0005: Documentos — armazenamento atrás de porta, cifrados em envelope

**Status:** Aceita
**Data:** 2026-08-31

### Contexto
O repositório de documentos por setor precisa decidir três coisas: onde os bytes ficam,
como são protegidos em repouso, e quem pode lê-los. A intranet é auto-hospedada por quem
adota, às vezes on-premise sem serviço de object storage, e a visibilidade de um documento é
por documento (só o setor, ou a empresa toda) — não por pasta.

### Decisão
**Armazenamento** atrás da porta `IArquivoStore`, com uma implementação em sistema de
arquivos. O caminho é montado dentro da implementação a partir do tenant corrente
(`{raiz}/{tenantId}/{ab}/{guid}`), nunca pelo chamador, e o nome é opaco — sem slug de setor,
que pode ser renomeado. Backends em nuvem (S3, Blob, GCS) implementam a mesma porta.

**Cifragem em envelope**: cada arquivo recebe uma chave própria; o conteúdo é cifrado com
ela em blocos de 1 MiB de AES-256-GCM, cada bloco com nonce e tag próprios e o índice mais a
marca de último bloco como dado associado. A chave do arquivo é embrulhada por uma chave
mestra e guardada na coluna `ds_chave_embrulhada`, no formato versionado `secco-enc:v1:` já
adotado pela plataforma (ADR-0025). A origem da chave mestra fica atrás de
`ChaveMestraOptions`: configuração agora, KMS depois.

**Autorização**: `{slug}-admin` publica e define a visibilidade, inclusive marcar como
empresa toda; `{slug}-user` lê o que é do setor. Arquivos **nunca** são servidos como
conteúdo estático nem têm endereço adivinhável — toda leitura passa por
`GET /documentos/{id}/download`, que avalia a visibilidade e faz streaming decifrado.

### Consequências
- Como a cifragem acontece na aplicação, um backend em nuvem enxerga apenas texto cifrado:
  trocar de armazenamento deixa de ser uma decisão de confiança.
- Rotacionar a chave mestra reescreve só a coluna da chave embrulhada — nenhum arquivo é
  reescrito. É também o que torna KMS viável: o que trafega são 32 bytes, não o documento.
- Blocos de 1 MiB, e não um bloco único, porque GCM só autentica no fim da mensagem: um
  bloco único obrigaria a carregar o arquivo inteiro em memória antes de confiar em qualquer
  byte.
- Documento inexistente e documento fora do alcance devolvem o **mesmo** erro: distinguir os
  dois revelaria a existência do documento a quem não pode vê-lo.
- Fica proibido expor a raiz do armazenamento por `UseStaticFiles` ou por qualquer rota que
  aceite caminho — a regra de visibilidade precisa ser avaliada a cada leitura.
- O `Content-Type` informado pelo cliente nunca é persistido: vale a extensão cruzada com a
  assinatura dos primeiros bytes.

---

## ADR-0006: Observabilidade e auditoria são consumidas da plataforma, nunca construídas aqui

**Status:** Aceita
**Data:** 2026-09-02

### Contexto
A expectativa era que a Intranet já viesse com o LogStream configurado para duas coisas:
registrar erros de uso e auditar o que o usuário fez na sessão. Nenhuma das duas acontece, e a
investigação mostrou que os motivos são diferentes. Para **erro**, o produto LogStream está
pronto e o `Secco.LogStream.Client` está publicado, mas o `AddLogStream()` — o provider de
`ILogger` que a ADR-0008 da plataforma promete, com fila local, batch e retry — **não existe**:
não há pasta `Logging/` no `Secco.SDK.AspNetCore` e o nome só aparece em ADR e comentários. Para
**auditoria**, o problema é de modelo: `LogEntry` tem `Level`, `Message`, `StackTrace`,
`CorrelationId` e `CreatedAt`, e **não tem ator** — mesmo com o LogStream integrado, "quem fez o
quê" só caberia dentro do texto da mensagem. `Secco.Audit` não existe como código.

A tentação em ambos os casos é resolver localmente: um `ILoggerProvider` caseiro sobre o client,
e uma tabela `tb_auditoria` no banco do tenant. As duas foram avaliadas e recusadas.

### Decisão
Observabilidade e auditoria são **capacidades da plataforma**. Este produto consome, não
constrói:

- **Log de aplicação** sai por `ILogger<T>` e chega ao LogStream pelo `AddLogStream()` do SDK,
  quando ele existir ([issue #1](https://github.com/rafsecco/secco-platform/issues/1)). Até lá o
  `ILogger` local continua funcionando e nada é enviado.
- **Trilha de ação de usuário** será consumida do recurso que a plataforma criar
  ([issue #2](https://github.com/rafsecco/secco-platform/issues/2)). Nenhuma trilha local, nem
  provisória: sem tabela, sem porta, sem pontos de instrumentação.
- **`LogProcess`/`LogProcessDetail` fica reservado a processo de negócio com passos definidos** —
  o consumidor previsto é o motor de workflow da Fase 2 (`ProcessoDefinicao` → `Etapa` →
  `ProcessoInstancia`). Usá-lo como trilha de usuário é proibido.

### Consequências
- **A Intranet fica sem trilha de auditoria até a plataforma entregar o recurso.** É o custo
  aceito conscientemente, e está registrado no roadmap para não parecer esquecimento.
- A alternativa de uma tabela local temporária foi recusada por criar caminho de migração de
  dados no futuro — dado de auditoria migrado depois é dado cuja integridade ninguém consegue
  atestar.
- Fica proibido implementar aqui um `ILoggerProvider`, um cliente de log próprio ou qualquer
  persistência de log/auditoria em banco deste produto — inclusive "temporariamente". Quem
  precisar da capacidade abre issue no monorepo, conforme [`docs/plataforma.md`](../plataforma.md).
- Se a plataforma decidir que a auditoria vira um produto separado em vez de um recurso do
  LogStream, nada muda aqui: este produto não escolheu implementação, escolheu origem.

---

## ADR-0007: A Intranet não provisiona banco nem custodia credencial de provisionamento

**Status:** Aceita
**Data:** 2026-09-02

### Contexto
Duas situações reais de adoção pressionam nesta direção: o banco do cliente já existe e a
aplicação precisa de um usuário próprio; ou um tenant novo entra e alguém precisa criar o banco
dele. O caminho aparentemente mais curto é dar à aplicação um usuário com permissão de criar
databases — que é, na prática, o que o ambiente de desenvolvimento faz hoje ao usar SA no
`docker-compose.yml` e na connection string de exemplo do `.env.example`.

A plataforma não cobre isso: o único `CREATE DATABASE` do monorepo está na infraestrutura de
testes, e o AdminPortal cadastra a connection string de um banco que já existe, sem criá-lo. A
lacuna é real ([issue #3](https://github.com/rafsecco/secco-platform/issues/3)) — a questão é
quem a preenche.

### Decisão
A Intranet consome connection string do catálogo e nada mais. Criar database, criar login ou
usuário de banco e conceder permissão são capacidades da plataforma, que custodia o catálogo
cifrado (ADR-0025 da plataforma) e é o único lugar onde uma credencial privilegiada de banco
deve existir.

### Consequências
- Uma credencial com `dbcreator` ou `securityadmin` **nunca** é configurada neste produto —
  inclusive "só para criar o banco do tenant novo". Um privilégio desses no processo que atende
  requisição de usuário é o oposto do least privilege da ADR-0020 da plataforma.
- Enquanto a connection string de um tenant usar um usuário amplo, o isolamento físico da
  ADR-0005 é **convenção, não garantia**: um erro de connection string alcança o banco do tenant
  vizinho. Por isso o SA nos arquivos de desenvolvimento é **dívida registrada**, não padrão a
  copiar — a correção (usuário de aplicação com privilégio mínimo no compose) está no roadmap e
  independe da decisão da plataforma.
- Um painel dos bancos, se vier a existir aqui, se alimenta do catálogo e de health-check por
  banco com a conexão de runtime de cada tenant — nunca de uma credencial de servidor.
- Fica proibido a esta aplicação executar DDL fora das migrations do próprio schema de tenant.

---

## ADR-0008: Intranet é tenant do SecureGate e administra outros tenants

**Status:** Aceita
**Data:** 2026-09-12

### Contexto
Surgiu a dúvida de saber se a Intranet deveria ser, ela mesma, um tenant do SecureGate/
LogStream, ou se poderia usá-los diretamente sem tenant — motivada pela observação de que
"o único usuário da Intranet é a própria empresa", e por uma ideia nova: a Intranet gerenciar
a API do SecureGate/LogStream/NotificationHub **criando tenants** que representem **outros
sistemas que a própria empresa desenvolve**, administrando-os a partir de telas próprias.

A investigação do código e das ADRs dos dois repositórios mostrou que a resposta já existia,
espalhada em lugares nunca lidos juntos:

- `Setor = Role tenant-scoped` (ADR-0001) exige que a Intranet seja, ela mesma, um tenant do
  SecureGate — não há como ter Setor sem isso. `SecureGateSetorAccessProvisioner`
  (`src/Secco.Intranet.Infrastructure/Access/SecureGateSetorAccessProvisioner.cs`) já chama
  `client.CreateRoleAsync(tenantId, ...)` usando o `ITenantContext.TenantId` da requisição
  atual, através do client administrativo único e global da aplicação
  (`AddSecureGateAdminClient()`, `IntranetInfrastructureExtensions.cs:99`, credenciais em
  `Secco:SecureGate`).
- Todo método administrativo do `Secco.SecureGate.Client.Administration` (gerado por NSwag)
  recebe `tenantId` como **parâmetro por chamada** (`CreateRoleAsync(Guid tenantId, ...)`,
  `ListUsersAsync(Guid tenantId)`) — não é algo fixado no cadastro do client OAuth. Um único
  client, com escopo `securegate:admin`, opera tecnicamente sobre qualquer tenant.
- A plataforma já expõe `POST /api/v1/tenants` (criar tenant) e
  `POST /api/v1/tenants/{id}/databases/{product}/provisioning` (provisionar banco por
  produto, ADR-0028 da plataforma) — ambos liberados só por escopo OAuth `securegate:admin`,
  sem checar se o tenant do chamador bate com o tenant alvo.
- A issue [secco-platform#4](https://github.com/rafsecco/secco-platform/issues/4) já havia
  decidido, em 2026-09-04, exatamente este modelo: *"cada instalação é soberana — a Intranet
  é um produto que empresas baixam e rodam por conta própria; a multi-tenancy existe para
  que a empresa desenvolva outros produtos seus reaproveitando SecureGate e LogStream. Não é
  o portal da Secco — é o portal da empresa que adotou."* A mesma issue já responde à dúvida
  levantada aqui ("se não puder centralizar, não vejo sentido em ter tenant"): multi-tenancy
  compra SSO único, log num lugar só e superfície de operação cross-tenant — e só a terceira
  dependia de algo a construir. Faltava só a ADR formal do lado da plataforma, e o próprio
  texto da issue propõe escrevê-la "quando a área administrativa da Intranet começar, para
  nascer ancorada em código e não em intenção" — este é esse momento.
- `docs/roadmap.md` e `docs/plataforma.md`, neste repositório, descreviam o item "Área
  administrativa" como bloqueado por precisar "reabrir a ADR-0024 da plataforma" — premissa
  **incorreta**. A ADR-0024 real é um mecanismo mais estreito e não relacionado: um token de
  segunda via, sem claim `tenant_id`, só leitura, TTL curto, usado exclusivamente para busca
  de log cross-tenant pelo operador humano do `Secco.AdminPortal` (padrão de elevação estilo
  `sudo`, inspirado na RFC 8693). Criar ou administrar tenant/role/usuário não usa esse
  mecanismo — é liberado pelo escopo `securegate:admin` do client administrativo comum, que
  a Intranet já tem.

### Decisão
1. A Intranet continua sendo, ela mesma, um tenant do SecureGate — isso não muda. É o que
   sustenta Setor=Role e todo o modelo de dados/autorização próprio (Mural, Documentos,
   Setor). É a operação normal de uma empresa usando a Intranet para si.
2. A Intranet ganha uma capacidade adicional: usando o **mesmo** client administrativo já
   configurado, ela cria e gerencia **outros tenants** — representando outros
   sistemas/produtos internos que a própria empresa desenvolve sobre a plataforma — com
   telas próprias de administração. O `Secco.AdminPortal` fica reservado a quem adota a
   plataforma **sem** a Intranet.
3. Criar ou administrar tenant, role e usuário **não** usa elevação de token — só a busca de
   log cross-tenant (ADR-0024 da plataforma) usa, e é um recurso futuro e separado, fora do
   escopo desta ADR.
4. "Não faz sentido a Intranet ter tenant" é verdade só como fato operacional de **uma**
   empresa (ela sempre terá exatamente 1 tenant "de si mesma" na prática) — não é motivo
   para remover a máquina de multi-tenancy, que segue necessária tanto para o modelo
   Setor=Role quanto para permitir que outras empresas rodem sua própria instância da
   Intranet.
5. A Área administrativa é exclusiva de uma Role própria, **`intranet-admin`** — tenant-scoped
   na própria Intranet, sem relação nenhuma com `{slug}-admin` de setor. Nenhum outro
   usuário visualiza ou acessa essa área — nem um `{slug}-admin` de um setor, nem alguém que
   seja `{slug}-admin` de **todos** os setores. Ser admin de setor não aproxima de ser
   `intranet-admin`; são Roles independentes, e a tela nem aparece no menu para quem não tem
   a segunda. Vale hoje para a administração de tenants (criar, provisionar, gerenciar
   usuários/roles de outro tenant) e, mais adiante, para o recurso futuro **"Acessar como"**
   (admin acessando com o perfil de um usuário) — os dois ficam reservados ao
   `intranet-admin`, ninguém mais, nem outro tipo de admin.

### Consequências
- Um único client OAuth da Intranet passa a operar sobre N tenants — seguro porque a
  autorização do SecureGate é por escopo do client, não por identidade de tenant do
  chamador. Registrado aqui explicitamente para não ser lido como furo de segurança.
- Não depende de nenhuma capacidade nova da plataforma — o trabalho que falta é inteiramente
  do lado da Intranet (UI + handlers).
- `intranet-admin` é uma Role tenant-scoped nova, independente de qualquer `{slug}-admin` —
  a Área administrativa (tenants, e depois "Acessar como") checa especificamente essa Role,
  nunca "é admin de algum setor". Layout de tela e o restante do modelo de dados da feature
  ficam para a spec dedicada — mas o **quem pode acessar** já está decidido aqui, não é
  aberto para a spec revisitar.
- **Critério de aceite, já fixado para quando a feature for construída** (a spec detalha a
  UI, não este critério): testes de integração provando (1) usuário sem `intranet-admin` não
  vê nem acessa a Área administrativa, nem a rota direta; (2) um `{slug}-admin` — de um setor
  ou de vários — também é bloqueado, exatamente para provar que "admin de setor" não é atalho
  para `intranet-admin`; (3) `intranet-admin` acessa normalmente. O mesmo trio de teste se
  repete, sem exceção, no dia em que "Acessar como" for implementado: `{slug}-admin`
  bloqueado mesmo administrando o setor do usuário-alvo, `intranet-admin` liberado.
- Fica proibido a spec futura da Área administrativa revisitar essa fronteira de autorização
  por conveniência (ex: liberar para `{slug}-admin`) — a spec implementa a regra, não a
  redesenha.

### Escopo esperado (não normativo — orienta o brainstorm futuro, não é spec)
Primeiro corte da Área administrativa: criar tenant, ver status de provisionamento por
produto (SecureGate/LogStream/NotificationHub) por tenant, gerenciar usuários e roles de
qualquer tenant administrado — incluindo atribuir a Role `inventario-admin` (ver
Consequências revisadas da ADR-0001) — tudo reaproveitando o `ISecureGateClient`
administrativo já configurado em `IntranetInfrastructureExtensions.cs`, e tudo atrás de
`intranet-admin`. Fica reconhecido, sem resolver agora, que atribuir uma Role a um **grupo**
do AD/EntraID depende de federação AD/Entra no SecureGate — capacidade que ainda não existe
e já está registrada na Fase 4 do roadmap; segue no mesmo trilho, sem mudança.
