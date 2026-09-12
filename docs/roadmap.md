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
- [ ] Diretório organizacional (perfil de colaborador, organograma básico) — telas
      existem como demonstração, atrás de `Intranet:Demo:Habilitado`
- [x] Repositório de documentos por setor: upload cifrado em envelope, visibilidade por
      documento (só o setor ou a empresa toda), download autorizado e arquivamento (o
      registro e o arquivo permanecem) — ADR-0005
- [ ] Controle de inventário — recurso sem setor dono. Autorização por Role tenant-scoped
      própria (ex.: `inventario-admin`), atribuível a qualquer usuário do tenant — não
      pelo modelo `{slug}-admin` de setor da ADR-0001, porque o recurso não pertence a
      um setor específico (revisto em 2026-09-12; a primeira redação da ADR-0001 cogitava
      o setor Infraestrutura como "dono nato")
- [ ] `Recurso` + `SetorRecurso` (catálogo de módulos habilitáveis por setor)
- [ ] Tela de administração de setores (cadastro + toggle de recursos)
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
- [ ] `ItemMenu` — tabela autorecursiva + tela de montagem de menu com níveis, para
      recursos próprios que a instituição adotante desenvolver
- [ ] Área administrativa — cobre a gestão do próprio tenant (usuários, roles, setores) **e**
      a criação/administração de outros tenants, representando outros sistemas que a empresa
      desenvolve sobre SecureGate/LogStream/NotificationHub — decisão registrada na ADR-0008.
      Não depende de capacidade nova da plataforma: a API de tenants (`POST /api/v1/tenants`,
      provisionamento de banco por produto) já existe; falta só construir aqui. Exclusiva da
      Role `intranet-admin`, sem relação com `{slug}-admin` de setor. Link para
      [secco-platform#4](https://github.com/rafsecco/secco-platform/issues/4) mantido como
      referência histórica da decisão de modelo (2026-09-04), não mais como bloqueio

## Fase 3 — Operacional

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
- [ ] **Identidade corporativa e exposição de rede.** A intranet não deve ser alcançável de
      fora da rede da empresa — a restrição é de rede, não de código, e o produto não a
      implementa sozinho. Dentro da rede, o usuário deve ser reconhecido pelo diretório
      corporativo (Active Directory / Azure Entra ID) em vez de digitar credencial; uma tela
      de login entra só para quem estiver fora. Federar AD/Entra é capacidade do
      `Secco.SecureGate` (ADR-0006), não deste repositório: vira demanda de plataforma quando
      o desenho existir. A aplicação **tem** saída para a internet; o que não deve existir é
      entrada de fora

## Fase 5 — Comunidade (pós-lançamento open source)

- [ ] Motor de processos v2: etapas paralelas, condicionais
- [ ] Marketplace de temas (empacotamento + documentação para terceiros)
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
