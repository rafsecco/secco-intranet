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
- [ ] Envio de log ao LogStream: registrar o `AddLogStream()` do `Secco.SDK.Logging` 0.1.0,
      com bind lazy pela seção `Secco:LogStream` — presente, envia; ausente, o `ILogger` local
      segue sozinho. **Desbloqueado** em 2026-09-05
      ([secco-platform#1](https://github.com/rafsecco/secco-platform/issues/1))
- [x] Sistema de temas: RCL resolvida por `IViewLocationExpander`, contrato de tema em
      `Secco.Intranet.Web.Theming` e o tema de saída `Vertical` — ADR-0003/ADR-0004
- [ ] Tirar o SA do ambiente de desenvolvimento: script de init no `docker-compose.yml` criando
      usuário de aplicação com privilégio mínimo, e `.env.example` deixando de usar `sa` na
      connection string do tenant. Dívida registrada pela ADR-0007; independe da plataforma —
      mas o provisionamento do SecureGate 0.3.0 passa a ser a referência de como o privilégio
      mínimo é definido

## Fase 1 — MVP visível

- [ ] Mural de avisos/comunicados — rota e tela já existem; falta o recurso real
      (entidade, publicação, edição). Um recurso só, com discriminador de tipo
      (aviso, evento, notícia)
- [ ] Diretório organizacional (perfil de colaborador, organograma básico) — telas
      existem como demonstração, atrás de `Intranet:Demo:Habilitado`
- [x] Repositório de documentos por setor: upload cifrado em envelope, visibilidade por
      documento (só o setor ou a empresa toda), download autorizado e arquivamento (o
      registro e o arquivo permanecem) — ADR-0005
- [ ] Controle de inventário (dono nato: setor Infraestrutura, fixo)
- [ ] `Recurso` + `SetorRecurso` (catálogo de módulos habilitáveis por setor)
- [ ] Tela de administração de setores (cadastro + toggle de recursos)
- [ ] Central de notificação in-app (`AvisoUsuario`, sino + toast), consumindo o canal
      in-app do `Secco.NotificationHub` (já disponível)
- [ ] Trilha de auditoria de ação de usuário (quem baixou qual documento, quem publicou, quem
      entrou), consumindo a superfície `audit-entries` do `Secco.LogStream.Client` 0.3.0.
      **Desbloqueado** em 2026-09-05
      ([secco-platform#2](https://github.com/rafsecco/secco-platform/issues/2)). Exige rodada
      de desenho própria: o que auditar e onde instrumentar não estão decididos

## Fase 2 — Controle de processos (v1 simples)

- [ ] Motor de workflow linear: `ProcessoDefinicao` → `Etapa[]` → `ProcessoInstancia`,
      com prazo por etapa e notificação (in-app, via NotificationHub) aos setores
      envolvidos quando uma etapa inicia
- [ ] Onboarding de novo colaborador implementado como um processo (caso de teste real)
- [ ] Notificação ao usuário, ao abrir a intranet, de processos pendentes para ele
- [ ] `ItemMenu` — tabela autorecursiva + tela de montagem de menu com níveis, para
      recursos próprios que a instituição adotante desenvolver
- [ ] Área administrativa — **escopo não decidido**: administrar só o próprio tenant (usuários,
      roles, setores, via `Secco.SecureGate.Client`) ou também absorver a operação de plataforma
      cross-tenant do `Secco.AdminPortal`. A segunda opção reabre a ADR-0024 da plataforma, que
      dá ao operador uma identidade sem `tenant_id`, incompatível com um produto tenant-scoped.
      [secco-platform#4](https://github.com/rafsecco/secco-platform/issues/4)

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

## Fase 5 — Comunidade (pós-lançamento open source)

- [ ] Motor de processos v2: etapas paralelas, condicionais
- [ ] Marketplace de temas (empacotamento + documentação para terceiros)
- [ ] Segundo tema de saída oficial: `Secco.Intranet.Themes.Horizontal` (menu no topo) —
      o padrão (`Vertical`) sai antes, na Fase 0/1
- [ ] Automação opcional de clonagem de perfis admin/user por setor, se um caso de uso
      concreto justificar (ver ADR-0001)
- [ ] Pesquisas de clima organizacional, outros extras
- [ ] Padronizar o separador das tabelas em toda a documentação. Hoje o repositório usa a
      forma compacta (`|---|`) de ponta a ponta, e a regra `MD060` do markdownlint está
      desligada em `.vscode/settings.json` por isso. Se a padronização acontecer, a regra
      volta a ligar no mesmo commit — desligada sem plano, ela vira ruído permanente

---

## Fora de escopo por enquanto

- `Secco.Intranet.Api` separada — decisão de monolito (ADR-0002); só revisitar se um
  front separado (Angular/React/Blazor WASM) virar necessidade real
- Licença: MIT
