# Secco.Intranet — Roadmap

> Documento vivo — atualizar conforme o desenvolvimento avança (marcar itens concluídos,
> ajustar escopo). Decisões arquiteturais que sustentam este roadmap estão em
> [`docs/adr/secco-intranet-adrs.md`](adr/secco-intranet-adrs.md).

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
- [ ] Integração com `Secco.LogStream.Client` (logs)
- [ ] Sistema de temas: esqueleto RCL (`IViewLocationExpander`) com um tema padrão mínimo
      — ADR-0003

## Fase 1 — MVP visível

- [ ] Mural de avisos/comunicados
- [ ] Diretório organizacional (perfil de colaborador, organograma básico)
- [ ] Repositório de documentos por setor (upload, público/privado via `SetorRecurso`)
- [ ] Controle de inventário (dono nato: setor Infraestrutura, fixo)
- [ ] `Recurso` + `SetorRecurso` (catálogo de módulos habilitáveis por setor)
- [ ] Tela de administração de setores (cadastro + toggle de recursos)
- [ ] Central de notificação in-app (`AvisoUsuario`, sino + toast), consumindo o canal
      in-app do `Secco.NotificationHub` (já disponível)

## Fase 2 — Controle de processos (v1 simples)

- [ ] Motor de workflow linear: `ProcessoDefinicao` → `Etapa[]` → `ProcessoInstancia`,
      com prazo por etapa e notificação (in-app, via NotificationHub) aos setores
      envolvidos quando uma etapa inicia
- [ ] Onboarding de novo colaborador implementado como um processo (caso de teste real)
- [ ] Notificação ao usuário, ao abrir a intranet, de processos pendentes para ele
- [ ] `ItemMenu` — tabela autorecursiva + tela de montagem de menu com níveis, para
      recursos próprios que a instituição adotante desenvolver
- [ ] Área administrativa com os recursos do `Secco.AdminPortal`

## Fase 3 — Operacional

- [ ] Recurso de aviso de vencimento (`ItemVencimento` + `AvisoAntecedencia`, múltiplos
      avisos escalonados) — ex: renovação de certificado
- [ ] RH self-service (férias, holerites) — pode reaproveitar o motor de processos da
      Fase 2 (solicitação de férias = processo com etapas)
- [ ] Ferramentas do dia a dia (reserva de sala, links rápidos)

## Fase 4 — Integrações externas

- [ ] Chamados de TI com integração Jira/Zendesk/GitHub/Azure DevOps (conectores
      independentes, priorizar por demanda real)
- [ ] Analytics/auditoria mais robusta em cima do LogStream

## Fase 5 — Comunidade (pós-lançamento open source)

- [ ] Motor de processos v2: etapas paralelas, condicionais
- [ ] Marketplace de temas (empacotamento + documentação para terceiros)
- [ ] Segundo tema de saída oficial: `Secco.Intranet.Themes.Horizontal` (menu no topo) —
      o padrão (`Vertical`) sai antes, na Fase 0/1
- [ ] Automação opcional de clonagem de perfis admin/user por setor, se um caso de uso
      concreto justificar (ver ADR-0001)
- [ ] Pesquisas de clima organizacional, outros extras

---

## Fora de escopo por enquanto

- `Secco.Intranet.Api` separada — decisão de monolito (ADR-0002); só revisitar se um
  front separado (Angular/React/Blazor WASM) virar necessidade real
- Licença: MIT
