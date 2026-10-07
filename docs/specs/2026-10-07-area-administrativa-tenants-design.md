# Área administrativa de tenants — subsistema 1: ciclo de vida do tenant

**Data:** 2026-10-07
**Estado:** implementado (subsistema 1)

## Problema

A ADR-0008 decidiu que a Intranet, além de ser ela mesma um tenant do SecureGate, cria e
administra **outros tenants**: um por sistema que a empresa desenvolve sobre a plataforma. O caso
que guiou este desenho:

> A empresa vai construir um "Sistema de compras". Na Intranet, o `intranet-admin` cria um tenant
> para ele e liga só os recursos de que o sistema precisa: o LogStream, para o sistema gravar log
> e o admin ler os logs da Intranet e do Compras no mesmo lugar; e o SecureGate, se o sistema
> precisar de usuários e restrição de acesso, para o admin administrar os perfis do Compras pela
> Intranet.

A Intranet gerencia **apenas os recursos da plataforma**: SecureGate, LogStream e NotificationHub.
O banco do próprio "Sistema de compras" não é dela.

## Decomposição

O pedido cobre a criação do tenant **e** a administração contínua dele. São quatro subsistemas,
cada um com spec própria, nesta ordem:

| # | Subsistema | Depende de |
|---|---|---|
| **1** | **Ciclo de vida do tenant**: criar, adotar, ligar recursos, status, ativar/desativar (**esta spec**) | nada novo da plataforma |
| 2 | Acesso do tenant administrado: perfis e usuários, reaproveitando `/acesso` com o tenant como parâmetro | nada novo |
| 3 | Leitor de logs da Intranet e dos tenants administrados, por **elevação explícita** (ADR-0030/0031 da plataforma) | nada novo na plataforma; mexe na autenticação da Intranet |
| 4 | Credencial do sistema: emitir, rotacionar e revogar o client OAuth do "Sistema de compras" | **bloqueado** por [secco-platform#31](https://github.com/rafsecco/secco-platform/issues/31) |

O 1 é a fundação: sem tenant não há o que administrar. O 4 só pode existir depois da #31 (ver
"Segurança"). **Não se constrói paliativo.**

### Dois mecanismos de autorização, não um

| Operação | Mecanismo |
|---|---|
| Criar tenant, provisionar banco, ativar/desativar (este subsistema); criar perfis e atribuí-los em outro tenant (subsistema 2) | client de serviço da Intranet com `securegate:admin`, o mesmo do `/acesso`. A ADR-0030 da plataforma: *"Gestão administrativa comum não exige elevação"* |
| Ler log de outro tenant (subsistema 3) | elevação: o `intranet-admin` troca o próprio token por um token curto, só de leitura de log, sem `tenant_id` |

A elevação é **só leitura** e **só de log**. Ela não participa de nada que escreva no SecureGate.

## O que o levantamento apurou

Verificado no `secco-platform`, não presumido:

| Pergunta | Resposta |
|---|---|
| A API de tenants existe? | Sim, sob `securegate:admin`: `POST/GET /tenants`, `GET /tenants/{id}`, `POST …/activate` e `…/deactivate`, `PUT …/databases/{product}`, `POST …/databases/{product}/provisioning`, `GET …/databases/status` |
| Qual produto tem banco por tenant? | LogStream e NotificationHub. **O SecureGate não**: identidade é dado de plataforma (ADR-0022), um banco só com usuários e perfis por tenant. Ligar o SecureGate para um tenant não provisiona nada |
| O provisionamento cria o banco sozinho? | Só se o SecureGate tiver credencial privilegiada configurada. Sem ela, devolve um **script SQL com a senha gerada**, uma única vez, para um DBA aplicar (`TenantDatabaseProvisioningDto.Script`). Em modo script a connection string já entra no catálogo antes de o banco existir, e o painel de status é quem mostra esse intervalo |
| O status de banco usa credencial privilegiada? | Não: sonda com a conexão de runtime de cada tenant e devolve produto, alcançável e motivo classificado, nunca a connection string |
| Dá para excluir tenant ou desligar recurso? | **Não.** Não há `DELETE` de tenant nem de banco do catálogo |
| O `{product}` é validado? | Texto livre, até 50 caracteres, kebab-case. Cabe à Intranet não repassar texto do usuário |
| 2FA do `intranet-admin` é garantido? | **Não.** Obrigatório só para o `installation-operator` (`OperatorTwoFactorPolicy.cs`); voluntário para os demais. O token não traz `amr`. `GetUserAsync` expõe `twoFactorEnabled` |
| Token de client credentials carrega tenant? | **Não** (`TokenEndpoints.cs:73`): o tenant vem do header `X-Tenant-Id`. Base do bloqueio do subsistema 4 ([#31](https://github.com/rafsecco/secco-platform/issues/31)) |
| Isolamento entre tenants no banco do SecureGate | Só por convenção: `where TenantId == …` à mão, sem filtro global nem RLS. Endurecimento pedido em [#32](https://github.com/rafsecco/secco-platform/issues/32); não bloqueia esta spec |

## Decisões

| Eixo | Decisão |
|---|---|
| Quem acessa | `intranet-admin` (ADR-0008, sem exceção) **e** conta com 2FA ativo |
| Quais tenants a área enxerga | Só os do **cadastro local** `TenantAdministrado`: criados pela Intranet ou adotados explicitamente |
| Nunca administráveis | O tenant `instalacao` (Guid fixo da plataforma), o tenant da própria requisição e qualquer tenant que seja uma Intranet (tenha banco no catálogo do produto `intranet`) |
| Recursos | Lista fechada: `securegate`, `logstream`, `notificationhub`. O banco do sistema não é gerido aqui |
| Ligar recurso | Uma ação por recurso, na página do tenant, separada da criação |
| Script de provisionamento | Exibido uma vez, na resposta do próprio POST; nunca persistido nem registrado |
| Excluir tenant / desligar recurso | Fora: a plataforma não oferece. Lacuna registrada, sem issue (o caso de uso não pede) |
| Rastro | Toda escrita vai à trilha do LogStream, falha-aberta como o resto da auditoria (ADR-0006) |
| Desativação recusada pela plataforma (409, ex.: tenant de instalação) | Erro próprio `Tenants.DesativacaoRecusada`, não "indisponível" |

## Segurança

Avaliada antes do desenho, pensando em invasão. Nenhum ponto torna a funcionalidade inviável. O
crítico tem mitigação neste subsistema, e o que não tem mitigação possível aqui (subsistema 4)
fica bloqueado.

**🔴 Crítico: o raio de dano do `intranet-admin` cresce.** O client da Intranet já tem
`securegate:admin` sobre todos os tenants, e a ADR-0008 aceitou isso. Até aqui, porém, só o
`/acesso` do próprio tenant usava esse poder. Com esta área, quem toma a conta de um
`intranet-admin` passa a criar tenants, ativar e desativar sistemas e, com os subsistemas 2 e 3,
conceder perfis e ler log de todos eles. **Mitigação:** a área exige 2FA ativo. **Resíduo:**
login federado pelo Entra, em que o segundo fator é responsabilidade do Entra e a Intranet não o
enxerga; a correção completa (`amr` no token ou 2FA obrigatório por perfil) é da plataforma e
fica como sugestão para quando o subsistema 3 existir.

**🟠 Alto: tenant escolhido pela URL (IDOR).** Toda rota recebe `{tenantId}`. **Mitigação:** o
cadastro local é a lista de permissão, conferida por filtro antes de qualquer chamada à
plataforma. Fora do cadastro a resposta é **404**, nunca 403, para não confirmar que o tenant
existe.

**🟠 Alto: adoção indevida.** O "adotar" lista tenants do SecureGate. Sem regra, traria o tenant
`instalacao`, o da própria Intranet ou o de **outra Intranet** da mesma instalação (filial). As
exclusões da tabela de decisões valem **na Application**, não só na lista da tela: o POST com um
Guid forjado também é recusado.

> O cadastro não é fronteira de segurança **entre duas Intranets da mesma instalação**: as duas
> usam clients com `securegate:admin` sobre a instalação inteira (ADR-0030, instalação soberana).
> Ele protege contra erro e contra URL forjada dentro de uma Intranet, não contra o operador da
> instalação.

**🟠 Alto: segredo na tela.** O script do modo DBA traz a senha `db_owner` do banco do tenant.
Regras: renderizado na resposta do POST, sem redirect; `Cache-Control: no-store`; nunca em
TempData, cookie, banco, log ou trilha. A trilha registra só que um script foi gerado, para qual
tenant e recurso.

**🟡 Médio: produto arbitrário.** `{recurso}` da rota é mapeado de uma lista fechada para o nome
do produto. O texto recebido nunca chega à plataforma.

**🟡 Médio: desativação acidental.** Desativar corta login e catálogo do sistema em até um TTL de
cache. A confirmação exige digitar o slug do tenant.

## Modelo

`TenantAdministrado` (Domain), no banco do tenant da própria Intranet:

| Propriedade | Coluna | Observação |
|---|---|---|
| `Id` | `id_pk_tenant_administrado` | |
| `TenantId` (Guid) | `tenant_id` | Id do tenant no SecureGate. Sem prefixo, pelo mesmo motivo de `usuario_id` em `tb_perfis_colaboradores`: a convention não prefixa Guid que não é chave. Único (`uk_tenants_administrados_tenant_id`) |
| `Sistema` (string, 120) | `ds_sistema` | Nome do sistema que o tenant representa, ex. "Sistema de compras" |
| `Responsavel` (string, 120) | `ds_responsavel` | Texto livre |
| `Origem` (enum `Criado`/`Adotado`) | `ie_origem` | |
| `SecureGateHabilitado` (bool) | `fl_secure_gate_habilitado` | Liga as telas do subsistema 2 para este tenant |
| `RegistradoPor` (string, 200) | `ds_registrado_por` | Rótulo do ator, o mesmo da trilha |
| `CreatedAt` / `UpdatedAt` | `dt_created_at` / `dt_updated_at` | `BaseEntity` |

Tabela `tb_tenants_administrados`, nome pela convention. Constraints e índice nomeados
explicitamente na migration, nos dois providers.

**Nome, slug e situação (ativo) não são copiados.** Vêm do SecureGate a cada leitura. Uma cópia
local divergiria na primeira ativação feita por fora.

**Quais recursos estão ligados também não é estado local**, exceto o SecureGate: LogStream e
NotificationHub são lidos do `databases/status` da plataforma. Ligar o SecureGate é decisão da
Intranet (habilita telas), e por isso é a única marca local.

## Arquitetura

```text
Web (TenantsController)
  ├─ [SomenteIntranetAdmin] ─ [ExigeSegundoFator] ─ [TenantAdministrado]  (filtros)
  └─► handlers (Application/Tenants) ──► IGestaoDeTenants ──► SecureGateGestaoDeTenants ──► ISecureGateClient
            │                        └─► ITenantsAdministrados (repositório, EF)
            └─► ITrilhaDeAuditoria
```

`IGestaoDeTenants` (Application) devolve DTOs próprios, nunca tipos do client gerado:

| Leitura | Escrita |
|---|---|
| `ListarTenantsAsync` (todos, para o "adotar") | `CriarTenantAsync(nome, slug)` |
| `ObterTenantAsync(tenantId)` | `ProvisionarAsync(tenantId, produto)` → aplicado ou script |
| `ObterStatusDosBancosAsync(tenantId)` | `AtivarAsync(tenantId)` / `DesativarAsync(tenantId)` |

O adaptador mapeia `ApiException`, `HttpRequestException` e o timeout do `HttpClient` para erros
de negócio, como os adaptadores de acesso já fazem; `409` e `404` têm mensagem própria. Sem
SecureGate configurado, `GestaoDeTenantsIndisponivel` (no-op) responde "não configurado".

O segundo fator é lido pela porta que já existe, `IGestaoDeAcesso.ObterUsuarioAsync`
(`UsuarioDetalheDto.DoisFatoresAtivo`) — a área de tenants não ganha porta própria para isso.

Os tenants que são Intranet saem do catálogo do produto `intranet` (`ITenantCatalog`), que a
Intranet já consome.

## Regras dos handlers

Checadas antes de chamar a porta e devolvidas como `Result`:

1. **Tenant fora do cadastro não existe** para nenhuma operação além de criar e adotar.
2. **Não adotáveis:** `instalacao`, o tenant da requisição, tenants de Intranet e tenants já no
   cadastro.
3. **Criar:** cria no SecureGate e depois grava o cadastro. Se a gravação falhar, o erro diz
   "tenant criado e não registrado; use Adotar" e traz o slug. Não há transação distribuída: a
   falha é visível e recuperável pelo próprio fluxo.
4. **Ligar recurso:** só `logstream` e `notificationhub` provisionam. `securegate` só marca o
   cadastro. Recurso já ligado (status "cadastrado") não é reprovisionado pela tela.
5. **Provisionar** pede o alvo padrão (`Target` vazio) e nomes derivados pela plataforma. A tela
   não oferece servidor nem nome de banco: escolher servidor arbitrário a partir da Intranet é
   superfície desnecessária.
6. **Desativar** exige o slug digitado igual ao do tenant.

## Autorização

- `[SomenteIntranetAdmin]` na classe, o mesmo filtro e a mesma ordem do `AcessoController`. O
  trio de testes da ADR-0008 vale para todas as rotas.
- **`[ExigeSegundoFator]`** na classe, depois do anterior. Consulta `IGestaoDeAcesso.ObterUsuarioAsync` do
  usuário logado, no tenant da Intranet, com cache de 60 s por `sub`. **Fail-closed:** SecureGate
  indisponível fecha a área. Sem 2FA, renderiza uma tela própria explicando o motivo, com link
  para o cadastro de 2FA no SecureGate. No modo aberto de DEV (sem SecureGate configurado, só em
  `Development`), a área inteira já mostra "não configurado" e o filtro não tem o que consultar.
- **`[TenantAdministrado]`** nas actions com `{tenantId}`: confere o cadastro e devolve 404.
- Todo POST com `[ValidateAntiForgeryToken]`.
- Item de menu **Tenants** no grupo Administração, só para `intranet-admin`.

## Telas

Só parciais do contrato de tema (ADR-0004), nos dois temas oficiais, claro e escuro.

| Rota | Conteúdo |
|---|---|
| `GET /tenants` | Lista: sistema, nome e slug (do SecureGate), ativo, recursos ligados, origem. Ações Novo e Adotar |
| `GET/POST /tenants/novo` | Sistema, responsável, nome e slug do tenant |
| `GET/POST /tenants/adotar` | Tenants adotáveis, mais sistema e responsável |
| `GET /tenants/{tenantId}` | Dados, painel dos três recursos (ligado, ligado sem responder, não ligado; motivo classificado) e ações |
| `POST /tenants/{tenantId}/recursos/{recurso}` | Liga o recurso. Em modo script, a resposta é a tela de exibição única |
| `POST /tenants/{tenantId}/ativar` e `…/desativar` | Desativar com confirmação por slug |

> A rota é `/tenants`, e não `/administracao/tenants` como no primeiro rascunho: o setor mora na
> raiz da URL, e uma empresa com o setor "Administração" (slug `administracao`) perderia a página
> dele. `tenants` entrou em `SlugsReservados`.

## Auditoria

Na trilha do LogStream (ADR-0006), falha-aberta, com o ator humano. Na plataforma, o ator dessas
chamadas é o client da Intranet, e por isso **a trilha da Intranet é o único registro de quem
fez**.

| Verbo | Alvo |
|---|---|
| `tenant.criar` | tenant, sistema |
| `tenant.adotar` | tenant, sistema |
| `tenant.recurso.ligar` | tenant, recurso, aplicado ou script |
| `tenant.recurso.script-gerado` | tenant, recurso. **Nunca** o script |
| `tenant.ativar` / `tenant.desativar` | tenant |

## Testes

- **Autorização:** o trio da ADR-0008 em todas as rotas (sem perfil; `{slug}-admin` de um setor e
  de vários; `intranet-admin`). `intranet-admin` **sem 2FA** bloqueado. SecureGate indisponível
  fecha a área.
- **IDOR:** `tenantId` fora do cadastro dá 404 em toda rota com `{tenantId}`. Adotar `instalacao`,
  o próprio tenant, um tenant de Intranet ou um já cadastrado é recusado por POST direto, com
  Guid forjado. `{recurso}` fora da lista é recusado.
- **Segredo:** em modo script, o script aparece na resposta do POST com `no-store` e não aparece
  na trilha, em log capturado nem em TempData.
- **Falha parcial:** criação com sucesso remoto e falha na gravação local devolve a mensagem de
  recuperação com o slug.
- **Unidade:** regras dos handlers sobre fakes de `IGestaoDeTenants` e `ITenantsAdministrados`.
- **Migration** nos dois providers, com os nomes de constraint e índice.
- **Fumaça opt-in** contra o SecureGate real (criar, provisionar em modo script, status,
  desativar), no padrão da fumaça de acesso existente.

## Fora de escopo

- Subsistemas 2, 3 e 4, cada um com spec própria.
- Excluir tenant e desligar recurso (a plataforma não oferece).
- Escolher servidor, nome de banco ou login no provisionamento.
- Exportar ou excluir os dados de um tenant (citado como desejável na #32).

## Notas de implementação

- O teste de fumaça deixa um tenant desativado por execução, porque a plataforma não exclui tenant.

## Documentação a atualizar junto

- `docs/roadmap.md`: o item "Área administrativa" da Fase 2 passa a apontar para esta spec e a
  listar os quatro subsistemas, com o 4 marcado ⛔ pela #31.
- `docs/plataforma.md`: linhas para [#31](https://github.com/rafsecco/secco-platform/issues/31)
  e [#32](https://github.com/rafsecco/secco-platform/issues/32).
- `README.md`: passo de instalação "o `intranet-admin` precisa de 2FA para a área de tenants".
