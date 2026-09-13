# Controle de Inventário — desenho do recurso

**Data:** 2026-09-13
**Estado:** aprovado, pronto para plano de implementação

## Problema

O roadmap da Fase 1 previa Inventário como recurso do setor Infraestrutura (`Fixo = true`,
"dono nato"). Essa amarração foi revista em 2026-09-12 (ADR-0001) porque nada do recurso
tinha sido construído ainda: Inventário não pertence a setor nenhum, e sua autorização usa
uma Role tenant-scoped própria — o primeiro recurso do produto a sair do molde
`{slug}-admin`/`{slug}-user` do ADR-0001.

Este documento fecha o desenho: o que o item de inventário guarda, quem administra, e o que
fica de fora por depender de uma capacidade que a plataforma ainda não tem.

## Achado que mudou o escopo do v1

O desenho original previa que um admin pudesse conceder acesso de leitura a usuários
específicos ("atribuir perfil de acesso"). Levantamento no `secco-platform` mostrou que
**não existe, hoje, nenhuma forma de atribuir ou revogar um Role de um usuário já
existente** — só na criação (`POST /api/v1/tenants/{tenantId}/users`, campo `roles`).
Confirmado em três lugares: o endpoint (`UserEndpoints.cs`, só `POST`/`GET`), o client
gerado (sem método de atribuição), e o próprio `Secco.AdminPortal` — que só tem "criar
usuário com roles" e "editar o que uma role concede", nunca "atribuir role a alguém que já
existe". Aberta [secco-platform#26](https://github.com/rafsecco/secco-platform/issues/26).

Isso não é lacuna nova: é a mesma limitação que o Setor sempre teve, só nunca tinha ficado
visível — `SecureGateSetorAccessProvisioner` cria a Role `{slug}-admin` em si, mas nunca
vincula um usuário a ela (ADR-0001 já registrava isso como "a critério de quem adota").

**Decisão:** o v1 nasce sem tela de concessão de acesso. A autorização usa duas Roles fixas
— `intranet-admin` (superusuário da instalação, ADR-0008) e `inventario-admin` — e o código
já fica pronto para a segunda ser atribuída a alguém quando a #26 for entregue, sem precisar
de mudança aqui.

## Decisões

| Eixo | Decisão |
|---|---|
| Setor dono | Nenhum. `SetorId` é campo informativo opcional ("este item está com o Financeiro"), não dá autorização |
| Autorização | `intranet-admin` OU `inventario-admin` administram (ver e mutar); ninguém mais enxerga a tela, nem o item de menu |
| Visibilidade | Sem "leitura para todo mundo" — corrigido durante o brainstorm; é admin-only até a #26 permitir delegar |
| Código de patrimônio | Texto livre, sem unicidade forçada |
| Item baixado | Some da lista padrão; um filtro/aba mostra baixados (mesmo padrão de Setor inativo/Documento arquivado) |
| Histórico de movimentação | Fora do v1 (YAGNI) — só o estado atual (`AtribuidoA`) é guardado, não quem teve o item antes |
| Auditoria | Verbos no LogStream, seguindo o padrão já existente (Mural/Documentos/Setor) |

## Modelo de domínio

`ItemDeInventario` (entidade rica, construtor validando invariantes — mesmo padrão de
`Documento`/`Setor`):

| Campo | Obrigatório | Observação |
|---|---|---|
| `Nome` | Sim | Ex: "Notebook Dell Latitude" |
| `Descricao` | Não | Texto livre |
| `Categoria` | Não | Texto livre — sem enum fechado até um filtro estruturado ser necessidade real |
| `CodigoPatrimonio` | Não | Livre, sem unicidade |
| `SetorId` | Não | Informativo, FK opcional para `Setor` |
| `Status` | Sim | `Disponivel` \| `EmUso` \| `EmManutencao` \| `Baixado` — máquina de estado explícita, não derivada de data |
| `AtribuidoAUsuarioId` | Não | Guid do SecureGate |
| `AtribuidoANome` | Não | Nome em cache no momento da atribuição — mesmo padrão de `Documento.CriadoPor`, para não duplicar identidade (ADR-0006) |
| `CreatedAt` | Sim | Padrão já usado em toda entidade |

Métodos de transição (cada um sua regra, como `Setor.Ativar()`/`Desativar()`), com uma regra
única e simples cobrindo todos: **`Baixar()` é terminal — qualquer outro método lança
`DomainInvariantException` num item `Baixado`.** Fora essa regra:

- `Atribuir(usuarioId, nome)` — define `AtribuidoA*` e leva a `EmUso`. Válido a partir de
  `Disponivel` ou `EmUso` (reatribuir para outra pessoa substitui o `AtribuidoA*` anterior;
  não guarda quem teve antes — ver "Histórico de movimentação" em Fora de escopo)
- `Desatribuir()` — limpa `AtribuidoA*` e volta a `Disponivel`. Só válido a partir de `EmUso`
- `EnviarParaManutencao()` — leva a `EmManutencao` a partir de qualquer estado não-terminal,
  sem mexer em `AtribuidoA*` (o item pode voltar para a mesma pessoa)
- `VoltarDaManutencao()` — de `EmManutencao` para `EmUso` (se `AtribuidoA*` preenchido) ou
  `Disponivel` (se não)
- `Baixar()` — terminal, a partir de qualquer estado

## Autorização

Novo helper `AcessoAdministrativo` (ao lado de `SetorAcesso`, mesmo namespace
`Secco.Intranet.Web.Navigation`), generalizando o padrão para roles fixas (não derivadas de
slug):

```csharp
public const string RoleIntranetAdmin = "intranet-admin";

public static bool TemAcesso(ClaimsPrincipal? usuario, string roleEspecifica) =>
    usuario is not null
    && usuario.FindAll(SeccoClaims.Role).Any(c =>
        string.Equals(c.Value, RoleIntranetAdmin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(c.Value, roleEspecifica, StringComparison.OrdinalIgnoreCase));
```

`InventarioController` usa `TemAcesso(User, "inventario-admin")` para toda ação (inclusive
listar) — mesma dispensa de checagem em modo aberto de DEV que `SetorController.PodePublicar`
já usa (`!IntranetAuthenticationExtensions.IsConfigured(configuration) || TemAcesso(...)`).

Este helper é a primeira implementação real de `intranet-admin` no código — a ADR-0008 só
tinha documentado a decisão. Fica pronto para a Área administrativa reaproveitar depois,
provavelmente com uma checagem mais estrita (só `RoleIntranetAdmin`, sem OR, porque aquela
área é exclusiva — ver ADR-0008).

## Web

- `InventarioController`, paginado (mesmo padrão de `SetoresController`).
- Item de menu fixo "Inventário", só renderizado quando `TemAcesso(User, "inventario-admin")`
  — diferente de Mural/Diretório, que são sempre visíveis.
- Toda action (`Index` incluído) devolve 403/redirect para quem não tem acesso — não é só o
  botão escondido na tela; a rota em si é protegida.

## Persistência

`tb_itens_inventario`, migrations nos dois providers (SqlServer/Postgres). Convention global
do EF Core deriva os prefixos (`id_pk_item_inventario`, `ds_nome`, `ds_descricao`,
`ds_categoria`, `ds_codigo_patrimonio`, `id_fk_setor`, `ie_status`,
`id_fk_atribuido_a_usuario`, `ds_atribuido_a_nome`, `dt_created_at`). `ie_status` como int
(padrão do EF Core para enum, sem `HasConversion` — mesmo comportamento das outras entidades
do produto). Índice em `ie_status`: é o filtro mais comum da listagem.

## Auditoria

Verbos novos em `VerbosDeAuditoria`: `inventario.criar`, `inventario.editar`,
`inventario.atribuir`, `inventario.baixar`. Recurso: `inventario`.

## Testes

- Unitários: uma classe por handler (`CriarItemInventarioHandlerTests`,
  `AtribuirItemInventarioHandlerTests`, etc.), mesmo padrão de `CreateSetorHandlerTests`.
- Domínio: transições de estado — `Baixar()` é terminal, `Atribuir()` só parte de
  `Disponivel`/`EmUso`, etc.
- Integração, o ponto que mais importa aqui: prova que a autorização é real, não só visual.
  - Usuário sem `intranet-admin` nem `inventario-admin` recebe 403/redirect ao tentar `GET`
    a listagem e ao tentar qualquer `POST` — direto na rota, sem passar pela tela.
  - Usuário com `intranet-admin` acessa tudo.
  - Usuário com `inventario-admin` (sem `intranet-admin`) acessa tudo também — prova que a
    Role específica funciona sozinha, não só como complemento da role de instalação.

## Fora de escopo

- Tela de concessão de acesso ("atribuir `inventario-admin` a fulano") — bloqueada pela
  [secco-platform#26](https://github.com/rafsecco/secco-platform/issues/26). Quando a
  plataforma entregar o endpoint, isto vira avanço rápido: a autorização já está pronta,
  falta só a tela chamando o novo método do client. Ver [`docs/plataforma.md`](../plataforma.md).
- Histórico de movimentação (quem teve o item antes) — YAGNI, ver Decisões.
- Categoria como catálogo fechado — texto livre até um caso de uso real pedir filtro
  estruturado.
- Atribuir `inventario-admin` a um grupo do AD/EntraID — depende de federação AD/Entra no
  SecureGate, já registrada como demanda futura na Fase 4 do roadmap; mesmo trilho, sem
  mudança aqui.
