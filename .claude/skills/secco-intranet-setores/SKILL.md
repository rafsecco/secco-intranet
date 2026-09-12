---
name: secco-intranet-setores
description: Convenção de Setores/Departamentos e sua integração com autorização (Setor = Role tenant-scoped no Secco.SecureGate) do produto secco-intranet. Usar SEMPRE que a tarefa envolver cadastro de setor, vínculo de usuário a setor, perfis de acesso admin/user por setor, recursos sem setor dono (ex.: Inventário), ou qualquer verificação de "usuário pertence a este setor".
---

# secco-intranet — Setores e Autorização

Resume a decisão registrada em `docs/adr/secco-intranet-adrs.md` (ADR-0001). Ler a ADR
antes de decisões estruturais; este skill é o resumo operacional do dia a dia.

## Regra central: Setor = Role tenant-scoped

Não existe tabela de vínculo `Usuario↔Setor` própria da Intranet. Um setor com slug
`financeiro` cria automaticamente, no Secco.SecureGate, duas Roles tenant-scoped:

- `financeiro-admin`
- `financeiro-user`

Vincular um usuário a um setor é vincular ele a uma dessas Roles via `UserRole` do
SecureGate — nunca crie uma tabela nova pra isso. Um usuário com acesso a múltiplos
setores simplesmente recebe múltiplas Roles.

## Permissões seguem o padrão `recurso:ação` (ADR-0021 da plataforma)

As `RoleClaim` de cada perfil seguem o padrão já estabelecido pela plataforma:

```
intranet:{slug}:{recurso}:{ação}
```

Exemplo: `intranet:financeiro:documentos:editar`. O perfil `{slug}-admin` tende a ter
mais ações liberadas que o `{slug}-user` (que normalmente só tem `ver`), mas **não
automatize a criação/clonagem de claims entre os dois perfis** — isso foi decidido
explicitamente como manual, a critério de quem adota o sistema. Só automatizar se um
caso de uso concreto justificar.

## Setor "Fixo"

`Fixo` é um flag genérico da entidade `Setor` — o adotante marca um setor que não pode
ser removido pela tela de administração. Não amarra nenhum recurso a um setor
específico. Um setor fixo:
- Não pode ser desativado (`Setor.Desativar()` lança `DomainInvariantException`)
- Não deve ser excluível pela tela de administração — validar também na Application
  layer, não só confiar na regra de domínio

Hoje só o seeder de desenvolvimento ([SetoresDesenvolvimentoSeeder.cs](../../../src/Secco.Intranet.Infrastructure/Seeding/SetoresDesenvolvimentoSeeder.cs))
usa isso, marcando "Infraestrutura" como exemplo — não existe seed de migration, e
nenhum recurso é "dono" desse ou de nenhum outro setor fixo.

## Recursos sem setor dono

Nem todo recurso pertence a um setor (Inventário é o primeiro caso, ver roadmap). Um
recurso assim **não** segue o padrão `{slug}-admin`/`{slug}-user` — não há slug de setor
para derivar. Ele ganha uma Role tenant-scoped própria (ex.: `inventario-admin`),
atribuível a qualquer usuário do tenant via `UserRole`, do mesmo jeito que qualquer
outra Role do SecureGate — só que sem vínculo com a estrutura de setores.

## Ao adicionar um novo recurso habilitável por setor

Todo novo módulo que **pertence a um setor** (Documentos, Processos, Vencimentos...) e
precisa ser "habilitado por setor" passa pela tabela `SetorRecurso` (catálogo em
`Recurso`), nunca por uma flag solta na entidade `Setor`. Ver seção "Próximos recursos"
do README.

Nem todo recurso se encaixa nisso — antes de forçar um módulo em `SetorRecurso`,
pergunte se ele pertence a **um** setor ou é transversal à Intranet. O Inventário é
transversal: ver "Recursos sem setor dono" acima.
