# Modelo de permissões — desenho do recurso

**Data:** 2026-09-27
**Estado:** rascunho, aguardando revisão

## Problema

Hoje toda autorização do produto é por **nome de Role**: `SomenteIntranetAdminAttribute` compara
`intranet-admin`; `AcessoAdministrativo.TemAcesso` compara `inventario-admin`; `SetorAcesso` deriva o
slug do sufixo `-admin`/`-user`; `AcessoAoDiretorio` compara `diretorio-admin`/`diretorio-user`. Um
usuário que precise ler dez setores recebe dez Roles — cada uma viaja na sessão e no token.

A plataforma já resolve isso: cada Role carrega permissões no formato `recurso:acao`
(`Secco.SecureGate.Client` 0.11.0 expõe `RoleDto.Permissions`/`SetRolePermissionsAsync`), resolvidas
em runtime, fora do token, com revogação imediata (ADR-0021 da plataforma). A Intranet só **lê** isso
hoje (tela de perfil mostra a contagem e a lista, mas editar é "só leitura neste corte" — decisão da
spec da Área administrativa, 2026-09-23). Ninguém autoriza por permissão ainda.

Duas coisas concretas dependem disso, já combinadas com o dono do produto em 2026-09-24: um perfil
agrupador (`todos`) que dê leitura de todo setor e o Diretório numa Role só, em vez de onze; e um
nível de só-consulta no Inventário (`inventario:read`), que hoje não existe.

## O que o levantamento apurou

Verificado nos dois repositórios (código, não presumido):

| Pergunta | Resposta |
|---|---|
| A plataforma já tem um mecanismo de autorização por permissão pronto para ASP.NET Core? | **Sim.** `Secco.SDK.AspNetCore` (`AddSeccoAuthorization()`) registra um `IAuthorizationPolicyProvider` que transforma qualquer nome de policy no formato `recurso:acao` numa policy dinâmica — sem registrar nada por permissão. Resolve via `IPermissionResolver`, com cache obrigatório de TTL curto (`Secco:Authorization:CacheTtlSeconds`, padrão 60 s) e postura **fail-closed**: resolução indisponível nunca abre acesso |
| Isso exige o que a Intranet já evita (`AddSeccoAuthentication`, validação JWT de resource server)? | **Não.** São peças independentes. `AddSeccoAuthorization()` só precisa da claim `role` (já vem do `id_token, RoleClaimType = SeccoClaims.Role`) e do `ITenantContext` (já registrado via `AddSeccoTenancy()`). Dá para plugar sem tocar no login |
| Em produção, quem resolve `(tenant, role) → permissões`? | `AddSecureGatePermissionResolver()`, do `Secco.SecureGate.Client` — chama o SecureGate de verdade. Em DEV/Testing sem SecureGate, o resolvedor padrão lê de `IConfiguration` |
| Qual o formato exato de uma permissão? | `SeccoPermissions` (SharedKernel da plataforma): **um único `:`**, kebab-case minúsculo dos dois lados (`^[a-z0-9]+(-[a-z0-9]+)*:[a-z0-9]+(-[a-z0-9]+)*$`), até 100 caracteres. **Sem curinga** — não existe `setor:*:leitura` |
| O formato que a ADR-0001 já sugeria (`intranet:{slug}:{recurso}:{ação}`) é válido? | **Não** — tem três `:`, o formato real da plataforma aceita só um. A ADR-0001 escreveu isso como ilustração em 2026-07-19, antes de `SeccoPermissions` existir como está hoje; esta spec corrige o formato (ver "Decisões") |
| A Diretório spec (2026-09-24) já prometeu nomes de permissão? | **Sim** — `diretorio:read` (nível `Usuario`) e `diretorio:manage` (nível `Administrador`), com `intranet-admin` sempre implícito em `Administrador`. Esta spec entrega exatamente esses dois nomes, sem renomear |
| A Área administrativa spec já prometeu algum nome? | **Sim** — `inventario:read`, no cenário de aceite do perfil `todos` |
| Mural e Documentos autorizam como hoje? | Não por atributo de rota — **filtram**: `SetorAcesso.SlugsDoUsuario`/`AdministraSetor` decidem o que aparece e o que pode ser alterado, por dentro do handler/controller. Um gate de rota inteira não serve para migrar isso |

## Decisões

| Eixo | Decisão |
|---|---|
| Mecanismo | Reusar `Secco.SDK.AspNetCore` (`AddSeccoAuthorization()` + `AddSecureGatePermissionResolver()`), nunca reimplementar cache ou resolução |
| Como o produto consome | **Nunca** `[Authorize(Policy=...)]` direto no controller. Um atributo próprio, `ExigePermissaoAttribute`, no molde do `SomenteIntranetAdminAttribute` de hoje, chama `IAuthorizationService.AuthorizeAsync` por trás |
| `intranet-admin` | Continua **bypass por identidade**, fora do mecanismo genérico — não ganha as permissões gravadas, é reconhecido pelo nome (ADR-0008, "acesso a tudo automaticamente"). Evita esquecer de conceder uma permissão nova a ele no futuro |
| Ação global vs por setor | O formato real não aceita curinga, então "todos os setores, inclusive os futuros" é uma permissão própria (`setores:read`), checada em **OU** com a por-setor |
| Ação para setor | Inglês (`read`/`write`), como o exemplo da própria plataforma (`log-entries:write`) e como o Diretório já escolheu para o seu par (`read`/`manage`). Nenhuma permissão em português nasce nesta spec |
| Correção à ADR-0001 | O formato ilustrativo `intranet:{slug}:{recurso}:{ação}` é substituído por `setor-{slug}:{ação}` (uma palavra kebab de recurso, uma de ação). A geração deixa de ser manual: setor criado ou editado garante as duas permissões nas duas Roles |
| Como a permissão chega à Role | **Mesclagem, nunca substituição.** Ler o que a Role já tem, unir com o que esta spec garante, gravar a união — nunca apaga uma permissão extra que o `intranet-admin` tenha somado à mão pela tela |
| Backfill dos setores já existentes | Ação explícita "Reconciliar permissões" na Área administrativa, cobrindo de uma vez todo setor e as Roles do Diretório — sem fallback silencioso por nome de Role |
| Catálogo | Fixo, definido pelo produto; a tela de edição oferece lista marcável, nunca texto livre |
| Quem edita permissão de um perfil | `intranet-admin`, na tela de perfil já existente (`/acesso/perfil/{nome}`), sem trava — inclusive em perfis de setor e do Diretório |
| Inventário | **Não migra o administrativo.** `inventario-admin`/`intranet-admin` continuam por nome de Role; ganha só uma segunda porta de leitura, liberada também por `inventario:read` |
| `ItemMenu` | Fora de escopo — esta spec entrega o catálogo e a atribuição que aquela spec vai referenciar, não a entidade/tela |

## Catálogo de permissões

| Permissão | Concedida a | Onde é garantida |
|---|---|---|
| `setor-{slug}:read` | `{slug}-user`, `{slug}-admin` | Criar/editar o setor (mescla) |
| `setor-{slug}:write` | `{slug}-admin` | Criar/editar o setor (mescla) |
| `setores:read` | Qualquer perfil que o `intranet-admin` decidir (ex.: `todos`) | Manual, pela tela de perfil |
| `diretorio:read` | `diretorio-user`, `diretorio-admin` | Criar o perfil (convite da tela) + reconciliação |
| `diretorio:manage` | `diretorio-admin` | Criar o perfil (convite da tela) + reconciliação |
| `inventario:read` | Qualquer perfil que o `intranet-admin` decidir | Manual, pela tela de perfil |

Não nasce `setores:write` global nem `inventario:manage`: ninguém pediu esse caso agora, e o mesmo
formato permite somar depois sem migração. `inventario-admin` não vira permissão — continua Role.

## Mecanismo de autorização

### Gate de rota inteira — `ExigePermissaoAttribute`

Mesmo desenho do `SomenteIntranetAdminAttribute` já existente: `IAuthorizationFilter`,
`Order = int.MinValue` (roda antes do antifalsificação — 403, não 400, em `POST` sem token).

```text
ModoAbertoDeDev(ambiente, configuração)             → libera
AcessoAdministrativo.SomenteIntranetAdmin(usuário)  → libera
senão: IAuthorizationService.AuthorizeAsync(usuário, "recurso:acao")
       falhou                                        → 403
```

O `AuthorizeAsync` é resolvido pela policy dinâmica do `SeccoPermissionPolicyProvider` — nada é
registrado por permissão; o nome da constante do produto (ex.: `InventarioPermissoes.Leitura =
"inventario:read"`) já é a policy.

Usos: gate do Inventário (a nova entrada de leitura) e, futuramente, do `ItemMenu`.

### Filtragem — `IPermissoesDeSetor`

Mural e Documentos não bloqueiam a rota inteira: filtram o que aparece. Para eles, um serviço novo
na Application:

```csharp
Task<IReadOnlySet<string>> SlugsComPermissaoAsync(ClaimsPrincipal usuario, string acao, IReadOnlyList<string> setoresDoTenant, CancellationToken ct)
```

Resolve, para cada Role do usuário, o conjunto de permissões (mesmo cache do mecanismo acima); um
slug entra no resultado se a Role tiver `setor-{slug}:{acao}` **ou** a global `setores:{acao}`.
`intranet-admin` devolve todos os slugs, sem consultar nada. Substitui **todo** uso hoje de
`SetorAcesso.SlugsDoUsuario`/`AdministraSetor`/`SlugsAdministrados` — não só Mural e Documentos, mas
também `SetorController` (a página do próprio setor: publicações, "pode agir") e
`NavigationViewComponent` (quais setores aparecem no menu). Depois da migração `SetorAcesso` fica sem
chamador e sai do código — não existe uso legítimo restante de nome de Role para decidir visibilidade
ou escrita de setor.

### `AcessoAoDiretorio`

Troca só a fonte: `Nivel`/`TemNivel` deixam de olhar nome de Role e passam a resolver `diretorio:read`
(`Usuario`) e `diretorio:manage` (`Administrador`), com `intranet-admin` sempre `Administrador`. O
contrato público não muda — `ExigeNivelNoDiretorioAttribute`, o item de menu e "Meu perfil" continuam
chamando a mesma função, sem saber que a fonte trocou.

## Concessão e reconciliação

**Ao criar/editar um setor** (`SecureGateSetorAccessProvisioner`), depois de garantir as Roles
`{slug}-admin`/`{slug}-user` (já idempotente hoje), cada uma tem sua permissão **mesclada**: ler as
permissões atuais da Role, unir com `setor-{slug}:read` (as duas Roles) e `setor-{slug}:write` (só a
`-admin`), gravar a união. Nunca apaga o que já estava lá.

**Ao criar `diretorio-admin`/`diretorio-user`** pelo botão de conveniência da tela de Perfis (mesmo
padrão de "Criar intranet-admin"/"Criar inventario-admin" já existente), a mesma mesclagem garante
`diretorio:read` (as duas) e `diretorio:manage` (só a `-admin`).

**Reconciliar permissões:** uma ação nova na Área administrativa (tela de Perfis), fora de qualquer
perfil específico — varre todo setor do tenant e, se existirem, `diretorio-admin`/`diretorio-user`,
aplicando a mesma mesclagem de cada um. Cobre os setores cadastrados antes desta spec (sem nenhuma
permissão gravada hoje) e a migração do Diretório num só lugar. Idempotente: rodar de novo não muda
nada se já estiver tudo lá. Rastro de auditoria como qualquer escrita da Área administrativa.

Perfis comuns (`todos`, `gerente-de-compras` etc.) nunca são tocados pela reconciliação — só o
`intranet-admin`, à mão, decide o que eles carregam.

## Tela de edição de permissões

`/acesso/perfil/{nome}` ganha, abaixo da lista hoje só-leitura, uma lista marcável com o catálogo
inteiro (agrupado por recurso), habilitada para qualquer perfil, inclusive os de setor e do
Diretório — sem trava. Salvar chama `SetRolePermissionsAsync` com a seleção. Perfis reservados da
plataforma continuam fora da tela, como já é hoje.

## Migração dos consumidores

| Consumidor | Antes | Depois |
|---|---|---|
| Mural (visibilidade, "pode agir") | `SetorAcesso.SlugsDoUsuario`/`AdministraSetor` | `IPermissoesDeSetor.SlugsComPermissaoAsync` |
| Documentos (visibilidade, upload) | idem | idem |
| `SetorController` (página do setor, publicações) | `SetorAcesso.SlugsAdministrados`/`AdministraSetor` | idem |
| `NavigationViewComponent` (setores no menu) | `SetorAcesso.Visiveis`/`SlugsDoUsuario` | idem |
| Inventário (administrar) | `AcessoAdministrativo.TemAcesso(user, "inventario-admin")` | **Sem mudança** |
| Inventário (consultar) | não existe | nova entrada, `ExigePermissaoAttribute("inventario:read")` OR o acesso administrativo de hoje |
| Diretório | nome de Role em `AcessoAoDiretorio` | permissão, mesmo contrato público |
| Menu (`MostrarDiretorio`, `MostrarInventario`, setores visíveis) | nome de Role | permissão (via `AcessoAoDiretorio`/`IPermissoesDeSetor`/nova checagem do Inventário) |
| `SetoresController`, `AcessoController` (`SomenteIntranetAdmin`) | nome de Role | **Sem mudança** — ADR-0008 não é permissão, é identidade |

## Auditoria

Verbos novos em `VerbosDeAuditoria`, recurso `acesso`:

| Verbo | Metadata |
|---|---|
| `acesso.perfil-permissoes-editar` | nome do perfil e permissões **adicionadas**/**removidas** (não o valor final) |
| `acesso.permissoes-reconciliar` | quantos setores e quantas Roles do Diretório tiveram permissão adicionada |

## Testes

- **Unitários:** `SeccoPermissions`/formato não é reimplementado (é da plataforma); a mesclagem nunca
  perde uma permissão existente; `IPermissoesDeSetor` resolve por Role e por global corretamente;
  `intranet-admin` sempre devolve tudo sem chamar o resolvedor.
- **Integração — matriz por recurso**, no mesmo padrão da Área administrativa e do Diretório (`GET` e
  `POST`, gate antes do antifalsificação):
  - Mural/Documentos: usuário com `setor-x:read` só vê `x`; com `setores:read` vê todos, inclusive um
    setor criado depois da atribuição; sem nenhuma das duas, filtro vazio.
  - Inventário: `inventario:read` abre a leitura e continua 403 em escrever; `inventario-admin`
    continua liberado em tudo, sem precisar de `inventario:read`.
  - Diretório: perfil comum com só `diretorio:read` tem nível `Usuario`; com `diretorio:manage`,
    `Administrador`; a matriz da spec de 2026-09-24 continua valendo, agora por permissão.
  - Reconciliação: setor criado antes desta spec (sem nenhuma permissão) passa a responder depois de
    reconciliar; rodar duas vezes não duplica nem falha.
- **Fumaça:** acrescenta-se à do SecureGate real a resolução de permissão de um perfil comum contra
  `GetRoleAsync`/`SetRolePermissionsAsync` reais.

## Fora de escopo

- **`ItemMenu`** (entidade, CRUD, tela de montagem) — esta spec só entrega o que ele vai referenciar.
- **Grupos do AD/Entra como origem de perfil** ([#27](https://github.com/rafsecco/secco-platform/issues/27),
  [#28](https://github.com/rafsecco/secco-platform/issues/28)) — não existe na plataforma.
- **Nested/perfil-dentro-de-perfil** — descartado; o agrupamento é permissão-no-perfil (`todos`).
- **`setores:write` e `inventario:manage`** — mesmo formato, sem caso de uso agora.
- **Atualizar o texto da ADR-0001** para o formato real (`setor-{slug}:{ação}`) — vira uma tarefa do
  plano de implementação, não desta spec.
