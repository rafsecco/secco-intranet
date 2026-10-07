# Secco.Intranet — Roadmap

> Documento vivo — atualizar conforme o desenvolvimento avança (marcar itens concluídos,
> ajustar escopo). Decisões arquiteturais que sustentam este roadmap estão em
> [`docs/adr/secco-intranet-adrs.md`](adr/secco-intranet-adrs.md).
>
> Itens marcados ⛔ dependem de uma capacidade que o `secco-platform` ainda não oferece e que,
> por decisão registrada em ADR, não é implementada aqui — o inventário está em
> [`docs/plataforma.md`](plataforma.md).

**Critério de ordenação:** dependência técnica primeiro (o que outros módulos precisam),
depois complexidade crescente — módulos "CRUD simples" abrem caminho; o motor de
processos (maior risco técnico) só entra com a base já sólida.

---

## Fase 0 — Fundação

- [x] Scaffold do repositório (gerado a partir do template `secco-service`)
- [x] Recurso `Setor` (entidade, handlers, endpoints, testes) — ADR-0001
- [x] Projeto `Secco.Intranet.Web` (MVC) — 100% novo (não vinha do template), consumindo
      a Application layer em processo (ADR-0002)
- [x] Integração com `Secco.SecureGate.Client` (auth) — criação automática das Roles
      `{slug}-admin`/`{slug}-user` ao cadastrar um setor
- [x] Envio de log ao LogStream: `AddLogStream()` do `Secco.SDK.Logging` **0.2.0**, com bind
      pela seção `Secco:LogStream` — presente, envia; ausente, o `ILogger` local segue sozinho.
      O `Enabled` do pacote vem `true` por padrão e o validador então exige URL e credenciais,
      então a composição desliga o sink quando não há URL, senão a aplicação não subiria sem a
      seção ([secco-platform#1](https://github.com/rafsecco/secco-platform/issues/1))
- [x] Sistema de temas: RCL resolvida por `IViewLocationExpander`, contrato de tema em
      `Secco.Intranet.Web.Theming` e o tema de saída `Vertical` — ADR-0003/ADR-0004
- [x] Segundo tema de saída oficial, `Secco.Intranet.Themes.Horizontal` (menu no topo,
      paleta e tipografia próprias) — adiantado da Fase 5: um contrato provado por um único
      tema não está provado, e o produto já tinha consumidor suficiente (Mural, Documentos,
      administração de setor) para expor um vício de acoplamento ao Vertical, se houvesse
- [x] Tirar o SA do ambiente de desenvolvimento: serviço `sqlserver-init` no
      `docker-compose.yml` provisiona o banco do tenant e um login de aplicação com
      `db_owner` só nele — `sa` fica confinado a esse container efêmero, a aplicação nunca
      o vê. `.env.example` e a connection string do tenant passam a usar o login de
      aplicação. Segue o mesmo teto de privilégio do provisionamento real do SecureGate
      (ADR-0028 da plataforma). Renomeado de quebra: `secco_intranet_tenant_alfa` →
      `secco_intranet_alfa`, alinhando com o padrão que o template `secco-service` e os
      testes de integração já usavam

## Fase 1 — MVP visível

- [x] Mural de avisos: publicação por setor, com visibilidade, agendamento, expiração,
      edição e arquivamento — [spec](specs/2026-09-02-mural-design.md). Auditoria fica para
      o plano seguinte
- [x] Notificação do Mural: sino, e-mail e canal corporativo conforme a urgência, com
      entrega na data de entrada no ar e permalink por publicação —
      [spec](specs/2026-09-07-notificacao-mural-design.md)
- [x] Auditoria transversal: verbos de Mural, Documentos e Setor no `Secco.LogStream` —
      [spec](specs/2026-09-08-auditoria-design.md). Cobre publicar/editar/arquivar do Mural,
      publicar/arquivar de Documentos e criar/editar/desativar/reativar de Setor. Fica fora,
      por decisão registrada no spec: **quem baixou qual documento** (auditoria de leitura,
      recusada por volume) e **quem entrou** (login — nunca esteve no escopo deste item)
      (entidade, publicação, edição). Um recurso só, com discriminador de tipo
      (aviso, evento, notícia)
- [x] Diretório organizacional (perfil de colaborador, organograma básico) —
      [spec](specs/2026-09-24-diretorio-organizacional-design.md): perfil complementar local por
      usuário do SecureGate, busca de pessoas, "Meu perfil" e edição pelo admin (cargo, setor e
      gestor só do admin), organograma por gestor com regra de ciclo, importação CSV em duas etapas,
      e acesso só de `intranet-admin`/`diretorio-admin`/`diretorio-user`. O nome de exibição vem do
      SecureGate ([#30](https://github.com/rafsecco/secco-platform/issues/30)) —
      [spec](specs/2026-10-04-nome-de-exibicao-na-plataforma-design.md). A foto saiu do MVP: ver
      Fase 3
- [x] Repositório de documentos por setor: upload cifrado em envelope, visibilidade por
      documento (só o setor ou a empresa toda), download autorizado e arquivamento (o
      registro e o arquivo permanecem) — ADR-0005
- [x] Controle de inventário — recurso sem setor dono, autorização por Role tenant-scoped
      própria (`inventario-admin`, mais `intranet-admin` como superusuário da instalação —
      ADR-0008). Máquina de estado Disponível/Em uso/Em manutenção/Baixado; toda rota
      bloqueada para quem não tem a Role, não só o item de menu escondido —
      [spec](specs/2026-09-13-inventario-design.md). A tela de conceder `inventario-admin`
      vive na Área administrativa de acesso (#26 entregue), não aqui. Ganhou um nível de
      só-consulta, `inventario:read`, quando o modelo de permissões chegou (ver abaixo) —
      o administrativo continua só por `inventario-admin`
- [x] Recursos habilitáveis por setor e tela de administração deles —
      [spec](specs/2026-09-30-item-menu-por-setor-design.md). Entregue como árvore de itens de
      menu por setor (`ItemMenu`), que substituiu o `Recurso`/`SetorRecurso` previsto: ligar um
      recurso é existir um item ativo daquele tipo debaixo da raiz do setor. Documentos e Avisos
      são os dois tipos embutidos; itens "Personalizado" apontam para uma rota e podem ter
      filhos. Cadastro de setor escolhe os recursos (marcados por padrão); `/Setores/Menu/{id}`,
      exclusivo do `intranet-admin`, cria, liga/desliga, reordena e exclui itens. Desligar um
      recurso fecha leitura **e** escrita dele. A página do setor passou a exigir
      `setor-{slug}:read`. Setores de antes disso: "Reconciliar itens de menu" na lista de setores
- [x] Central de notificação in-app (sino + toast), consumindo o canal in-app do
      `Secco.NotificationHub`. O `AvisoUsuario` da redação original **não** vai existir:
      o Hub é dono do estado de lida, e uma cópia local violaria a ADR-0006. O toast virou
      o `Feedback` do contrato de tema, invocado uma vez pelo layout

## Fase 2 — Controle de processos (v1 simples)

- [ ] Motor de workflow linear: `ProcessoDefinicao` → `Etapa[]` → `ProcessoInstancia`,
      com prazo por etapa e notificação (in-app, via NotificationHub) aos setores
      envolvidos quando uma etapa inicia
- [ ] Onboarding de novo colaborador implementado como um processo (caso de teste real)
- [ ] Notificação ao usuário, ao abrir a intranet, de processos pendentes para ele
- [ ] `ItemMenu` além dos setores — a seção Setores do menu principal já vem da árvore de cada
      setor (setor = nível 0, submenus flutuantes, URL `/{setor}/{item}/…` —
      [spec](specs/2026-10-01-menu-principal-arvore-dos-setores-design.md)); falta, se um dia
      fizer sentido, os itens fixos (Mural, Diretório, Inventário, Administração) virarem nós
- [ ] Marketplace de temas (empacotamento + documentação para terceiros) — trazido da Fase 5.
      Os dois temas oficiais já provam o contrato; o que falta é o terceiro conseguir publicar e
      instalar o próprio tema sem abrir este repositório
- [x] Área administrativa de acesso, primeiro corte: perfis e usuários do próprio tenant,
      exclusiva do `intranet-admin`, com testes de autorização em toda rota —
      [spec](specs/2026-09-23-area-administrativa-acesso-design.md). Entregue: só o
      `intranet-admin` acessa (filtro declarativo na classe do controller); listagem de perfis
      e usuários, atribuir/retirar perfil, criar/excluir perfil, desativar/reativar usuário e
      encerrar sessões; o cadastro de setores passou a exigir o mesmo perfil. Seguem em specs
      próprias o modelo de permissões (autorizar por permissão, editar permissões de perfil,
      itens de menu com permissão por item) e o restante da Área administrativa abaixo
- [x] Modelo de permissões: autorização por `recurso:acao` (ADR-0021 da plataforma) em vez de
      nome de Role, em todo lugar que precisava — Mural, Documentos, a página do setor, o menu, o
      Diretório e uma nova entrada de leitura no Inventário —
      [spec](specs/2026-09-27-modelo-de-permissoes-design.md). Entregue: catálogo de permissões do
      produto; edição de permissões de um perfil na tela de acesso; `{slug}-admin`/`{slug}-user`
      e `diretorio-admin`/`diretorio-user` já nascem com a permissão do setor/Diretório (mesclagem,
      nunca substituição); ação "Reconciliar permissões" para os já cadastrados antes desta spec;
      `SetorAcesso` (checagem por nome de Role) saiu do código. Sem depender de nenhum
      desenvolvimento novo da plataforma — usa `Secco.SDK.AspNetCore` 0.8.3 e
      `Secco.SecureGate.Client` 0.11.0, já publicados. Fora deste corte: o `ItemMenu` em si
      (entidade, CRUD) e grupos do AD/Entra como origem de perfil
- [ ] Área administrativa — cobre a gestão do próprio tenant (usuários, roles, setores) **e**
      a criação/administração de outros tenants, representando outros sistemas que a empresa
      desenvolve sobre SecureGate/LogStream/NotificationHub — decisão registrada na ADR-0008.
      Não depende de capacidade nova da plataforma: a API de tenants (`POST /api/v1/tenants`,
      provisionamento de banco por produto) já existe; falta só construir aqui. Exclusiva da
      Role `intranet-admin`, sem relação com `{slug}-admin` de setor. Link para
      [secco-platform#4](https://github.com/rafsecco/secco-platform/issues/4) mantido como
      referência histórica da decisão de modelo (2026-09-04), não mais como bloqueio —
      [spec do subsistema 1](specs/2026-10-07-area-administrativa-tenants-design.md). Dividida em
      quatro subsistemas, cada um com spec própria; a área exige `intranet-admin` **com 2FA**:
  - [ ] 1. Ciclo de vida do tenant: criar, adotar, ligar recursos (SecureGate, LogStream,
        NotificationHub), status, ativar/desativar
  - [ ] 2. Perfis e usuários do tenant administrado, reaproveitando `/acesso`
  - [ ] 3. Leitor de logs da Intranet e dos tenants administrados, por elevação explícita
        (ADR-0030/0031 da plataforma)
  - [ ] 4. ⛔ Credencial do sistema administrado (client OAuth vinculado ao tenant) —
        [secco-platform#31](https://github.com/rafsecco/secco-platform/issues/31)

## Fase 3 — Operacional

- [ ] Foto do colaborador no Diretório — upload, recorte e redução pela SDK de imagem; bloqueado
      por [secco-platform#29](https://github.com/rafsecco/secco-platform/issues/29). Até lá, avatar
      por iniciais. Se a etapa avançar antes da SDK existir, a mitigação mínima registrada na
      [spec do Diretório](specs/2026-09-24-diretorio-organizacional-design.md) (assinatura de
      arquivo, nunca SVG, nome gerado pelo servidor, `nosniff`, EXIF não removido documentado) é
      obrigatória
- [ ] Recurso de aviso de vencimento (`ItemVencimento` + `AvisoAntecedencia`, múltiplos
      avisos escalonados) — ex: renovação de certificado
- [ ] RH self-service (férias, holerites) — pode reaproveitar o motor de processos da
      Fase 2 (solicitação de férias = processo com etapas)
- [ ] Ferramentas do dia a dia (reserva de sala, links rápidos)

## Fase 4 — Integrações externas

- [ ] Chamados de TI com integração Jira/Zendesk/GitHub/Azure DevOps (conectores
      independentes, priorizar por demanda real)
- [ ] Analytics em cima do LogStream. Depende da trilha de auditoria da Fase 1, que deixou de
      estar bloqueada em 2026-09-05
- [x] Login federado via Microsoft Entra ID por tenant (ADR-0026 da plataforma, já entregue
      lá desde 2026-07-19 — `TenantFederation`, `PUT /api/v1/tenants/{id}/federation`, app
      registration multi-tenant única, transparente para quem é relying party). Consumido
      aqui: aba Federação em `/acesso` (exclusiva do `intranet-admin`) liga/desliga e define o
      directory id do tenant — `Secco.SecureGate.Client` 0.11.0, sem precisar de versão nova.
      `IntranetAuthenticationExtensions` não muda nada: o SecureGate mostra o botão do Entra
      na própria tela de login dele e devolve o mesmo formato de token de sempre. Fora daqui:
      criar a app registration e configurar `SecureGate:EntraId` no host do SecureGate é
      operacional, não é código deste repositório
- [ ] **Identidade corporativa e exposição de rede.** A intranet não deve ser alcançável de
      fora da rede da empresa — a restrição é de rede, não de código, e o produto não a
      implementa sozinho. A aplicação **tem** saída para a internet; o que não deve existir é
      entrada de fora

## Fase 5 — Comunidade (pós-lançamento open source)

- [ ] Motor de processos v2: etapas paralelas, condicionais
- [ ] Idioma inglês no produto (i18n): hoje toda tela, mensagem e rótulo são só em português.
      Abrir o código não obriga isso agora, mas amplia quem consegue adotar e contribuir depois
      do lançamento open source
- [ ] Automação opcional de clonagem de perfis admin/user por setor, se um caso de uso
      concreto justificar (ver ADR-0001)
- [ ] Pesquisas de clima organizacional, outros extras
- [ ] Avaliar trocar o embrulho de chave do `EnvelopeCipher` pelo `ISeccoSecretCipher` do
      `Secco.SDK.EntityFrameworkCore` 0.4.0. O SDK promoveu o mesmo formato `secco-enc:v1:`
      que este produto implementou por conta própria — e o changelog da plataforma cita
      justamente esta duplicação. O formato é idêntico, então a troca não migra dado; o que
      precisa de cuidado é a cifragem do **conteúdo** em blocos, que é nossa e não tem
      equivalente lá
- [ ] Padronizar o separador das tabelas em toda a documentação. Hoje o repositório usa a
      forma compacta (`|---|`) de ponta a ponta, e a regra `MD060` do markdownlint está
      desligada em `.vscode/settings.json` por isso. Se a padronização acontecer, a regra
      volta a ligar no mesmo commit — desligada sem plano, ela vira ruído permanente

---

## Fora de escopo por enquanto

- `Secco.Intranet.Api` separada — decisão de monolito (ADR-0002); só revisitar se um
  front separado (Angular/React/Blazor WASM) virar necessidade real
- Licença: MIT
