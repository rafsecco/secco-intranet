# Contribuindo

Este arquivo é um roteador, não um manual: as regras vivem nas ADRs, e duplicá-las aqui só
criaria uma segunda fonte para divergir da primeira.

## A regra zero

**[`docs/adr/secco-intranet-adrs.md`](docs/adr/secco-intranet-adrs.md) é a fonte da verdade
deste produto.** Nenhum código contradiz uma ADR Aceita. Se a tarefa parece exigir isso, o
caminho não é contornar — é propor uma ADR nova que substitua a antiga. ADRs não são editadas
retroativamente.

Decisões do ecossistema (multi-tenancy, `Result<T>`, nomenclatura de banco, segurança) moram
nas ADRs do [`secco-platform`](https://github.com/rafsecco/secco-platform) e valem aqui
também.

## Antes de abrir o editor

| Você quer | Leia |
|---|---|
| Rodar o projeto | [`README.md`](README.md) |
| Saber o que está feito e o que vem | [`docs/roadmap.md`](docs/roadmap.md) |
| Entender uma decisão estrutural | [`docs/adr/secco-intranet-adrs.md`](docs/adr/secco-intranet-adrs.md) |
| Escrever ou alterar um tema | [`docs/temas.md`](docs/temas.md) |
| Saber o que este produto espera da plataforma | [`docs/plataforma.md`](docs/plataforma.md) |
| Ver o desenho de um recurso antes de implementá-lo | [`docs/specs/`](docs/specs/) |

## O que não se negocia

Um resumo do que mais tropeça na prática. O detalhe está nas ADRs citadas.

- **Setor é Role do SecureGate** (ADR-0001). Não existe tabela de vínculo usuário↔setor, e é
  proibido criar qualquer autorização fora do modelo de Role — inclusive uma flag
  `IsAdminDoSetor` numa tabela local.
- **Controller nunca acessa `DbContext` ou repositório** (ADR-0002). Sempre via handler. View
  recebe ViewModel, nunca entidade. Nenhuma regra de negócio em controller ou view.
- **View de página não escreve markup estrutural próprio** (ADR-0004). Sidebar, card e badge
  vêm dos parciais do contrato de tema; markup próprio cria página que tema novo não
  reestiliza.
- **Nenhuma trilha de auditoria local** (ADR-0006), nem provisória. A capacidade é consumida da
  plataforma. O mesmo vale para logging: nada de `ILoggerProvider` caseiro.
- **A aplicação não provisiona banco** (ADR-0007). Credencial com `dbcreator` ou
  `securityadmin` nunca é configurada aqui.
- **Arquivo de documento nunca é servido como conteúdo estático** (ADR-0005). Toda leitura passa
  pelo endpoint que avalia a visibilidade.
- **Nomenclatura de banco pela convention**, nunca digitada à mão. Constraints e índices, sim,
  nomeados explicitamente na migration.
- **Versões de pacote só em `Directory.Packages.props`**.
- **Toda feature acompanha teste no mesmo PR**; bug corrigido ganha teste que o reproduz.

## Capacidade de plataforma não se implementa aqui

Se a peça que falta é transversal — logging, auditoria, identidade, provisionamento —, ela é
pedida ao monorepo, não reescrita neste repositório. O procedimento está em
[`docs/plataforma.md`](docs/plataforma.md). O critério prático: **outro adotante precisaria da
mesma coisa?** Se sim, é da plataforma.

## Segurança é parte do design

Antes de implementar, e não na revisão, avalie explicitamente: confiança em input externo,
injeção, vazamento de informação, isolamento de tenant, autorização explícita e dependência
nova. Ao apresentar opções de design, inclua o risco de segurança de cada uma — não só o
trade-off funcional. Ver [`SECURITY.md`](SECURITY.md).

## Temas

Tema é pacote independente e não precisa de mudança no core. O contrato, a estrutura de pastas
e o piso de qualidade — responsivo, foco visível, claro e escuro — estão em
[`docs/temas.md`](docs/temas.md).

## Commits

[Conventional Commits](https://www.conventionalcommits.org/pt-br/):

```
feat(mural): publicação por setor com agendamento
fix(documentos): ordenação antes da projeção na listagem
docs(adr): registra o contrato de tema
```

Prefira explicar **por que** no corpo. O histórico é a única fonte que sobrevive quando a
memória de quem decidiu não está mais disponível.

## Antes de dizer que terminou

```bash
npm --prefix src/Secco.Intranet.Themes.Vertical run build   # se mexeu em scss
dotnet build                                                 # 0 avisos: warnings são erros
dotnet test
```

E atualize [`docs/roadmap.md`](docs/roadmap.md) se algum item de fase foi concluído.
