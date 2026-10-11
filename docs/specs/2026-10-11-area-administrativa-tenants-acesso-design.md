# Área administrativa de tenants — subsistema 2: perfis e usuários do tenant administrado

**Data:** 2026-10-11
**Estado:** proposta

## Problema

O subsistema 1 ([spec](2026-10-07-area-administrativa-tenants-design.md)) cria e adota tenants e liga
os recursos da plataforma. Ligar o SecureGate para o "Sistema de compras" hoje só marca o cadastro
(`fl_secure_gate_habilitado`): o tenant nasce vazio, sem perfis e sem ninguém que consiga entrar.
Este subsistema dá ao `intranet-admin` a gestão de acesso desse tenant pela Intranet: perfis e
permissões, cadastro de pessoas e o login federado pelo Entra ID.

## Escopo

| Grupo | Operações |
|---|---|
| Perfis | listar, criar, excluir (sem membros), detalhe com membros paginados |
| Permissões | editar a lista de permissões de um perfil (texto livre validado, ver "Permissões") |
| Usuários | listar com busca, detalhe, atribuir e retirar perfil, desativar e reativar, encerrar sessões |
| Cadastro | e-mail, nome de exibição, perfis iniciais e modo de entrada; reenviar convite |
| Federação | ler e definir o directory id do Entra e se está habilitada |

### Como as pessoas entram

O SecureGate é **fail-closed** no login federado (ADR-0026 da plataforma): a pessoa precisa existir no
tenant antes do primeiro login, e o Entra só prova a identidade. Por isso o **cadastro** é a única porta
de entrada, com dois modos:

| Modo | Na plataforma | Efeito |
|---|---|---|
| Senha por convite | `CreateUser` com `LocalLogin = true` | O SecureGate envia o convite de 72 h (ADR-0033) e a pessoa define a própria senha |
| Só pelo Entra | `CreateUser` com `LocalLogin = false` | Conta sem senha e sem convite; o primeiro login pelo Entra casa por e-mail dentro do directory id do tenant |

Os perfis informados no cadastro já são atribuídos pela plataforma na criação.

### Fora de escopo

- **Catálogo de permissões.** A ADR-0039 da plataforma (issue
  [#33](https://github.com/rafsecco/secco-platform/issues/33)) está em implementação e não publicada.
  A troca do texto livre por caixas agrupadas por produto é tarefa prevista, ver "Permissões".
- **Mapeamento grupo do Entra → perfil.** Sem a aplicação automática
  ([#28](https://github.com/rafsecco/secco-platform/issues/28) aberta), mapear não concede nada e
  confundiria quem configura. Entra junto com a mesma tela no `/acesso` da própria Intranet.
- **Convite no `/acesso` da própria Intranet.** Decisão de escopo: só o tenant administrado.
- Link de troca de senha e reset de 2FA disparados pelo admin.
- Proteção do "último administrador" do sistema administrado: a Intranet não sabe qual perfil é o de
  administrador de outro sistema.

## O que o levantamento apurou

Verificado no `secco-platform`, não presumido:

| Pergunta | Resposta |
|---|---|
| A plataforma confere que o usuário pertence ao tenant da rota? | **Sim.** `UserAccountService.AddRoleAsync` chama `BelongsToTenantAsync` e devolve "usuário não encontrado" (404) para conta de outro tenant. A Intranet não duplica a checagem; a fumaça prova o caso |
| Login federado cria conta no primeiro acesso? | **Não.** Pré-provisionado e fail-closed (ADR-0026) |
| `CreateUser` com `LocalLogin = false` envia convite? | **Não**: o convite só sai quando a conta entra por senha (`CreateUserHandler`) |
| O detalhe do usuário informa se há senha e se entra por senha? | Sim: `HasPassword` e `LocalLoginEnabled` em `UserDetailDto` |
| Excluir perfil usado por client do tenant | `409 SecureGate.Role.UsedByClients` (ADR-0037, #31) |
| Permissão desconhecida | Hoje aceita qualquer `recurso:acao`. Com a ADR-0039 publicada, `SetRolePermissions` recusa a acrescentada que nenhum catálogo declara |

## Arquitetura

O `/acesso` da Intranet não é reaproveitado literalmente: metade dele é convenção da própria Intranet
(perfis de setor, perfis do produto, "último `intranet-admin`", catálogo `IntranetPermissoes` e a
reconciliação), sem sentido no tenant de outro sistema. Reaproveita-se a mecânica (chamada ao client,
mapeamento de DTO e de erro), com uma **porta própria e o tenant explícito em toda assinatura**.

```text
Web: TenantsAcessoController  /tenants/{tenantId}/acesso/...
  [SomenteIntranetAdmin] → [ExigeSegundoFator] → [TenantAdministrado] → [SecureGateLigado]
        │
Application/Tenants/Acesso: handlers ──► IGestaoDeAcessoDoTenant (tenantId explícito)
        │                              └► ITrilhaDeAuditoria
Infrastructure/Access:
  SecureGateGestaoDeAcessoDoTenant ─┐
  SecureGateGestaoDeAcesso (atual) ─┴─► OperacoesDeAcessoNoSecureGate (núcleo interno)
  GestaoDeAcessoDoTenantIndisponivel  (sem SecureGate configurado)
```

Alternativas descartadas:

- **Parametrizar o `IGestaoDeAcesso` existente com `tenantId`.** Mexe em todos os handlers testados do
  `/acesso` e espalha `if (tenant == próprio)` para separar as regras da Intranet.
- **"Tenant alvo" escopado** lido pelo adaptador no lugar do `ITenantContext`. O tenant ficaria
  implícito: qualquer serviço resolvido no mesmo escopo passaria a operar em outro tenant sem que a
  assinatura mostre. Inaceitável numa área cujo risco principal é IDOR.

### Porta `IGestaoDeAcessoDoTenant`

Na Application (`Tenants/Acesso`). Todo método recebe `Guid tenantId` como primeiro parâmetro e devolve
`Result`, sem falha aberta (quem opera precisa saber que a ação não aconteceu).

| Leitura | Escrita |
|---|---|
| `ListarPerfisAsync` | `CriarPerfilAsync(nome)` / `ExcluirPerfilAsync(nome)` |
| `ObterPerfilAsync(nome)` | `DefinirPermissoesDoPerfilAsync(nome, permissoes)` |
| `ListarMembrosAsync(nome, pagina)` | `AtribuirPerfilAsync(usuarioId, perfil)` / `RetirarPerfilAsync(...)` |
| `ListarUsuariosAsync` | `CadastrarUsuarioAsync(email, nome, perfis, entraPorSenha)` → id |
| `ObterUsuarioAsync(usuarioId)` | `ReenviarConviteAsync(usuarioId)` |
| `ObterFederacaoAsync` | `DesativarUsuarioAsync` / `ReativarUsuarioAsync` / `EncerrarSessoesAsync` |
| | `DefinirFederacaoAsync(directoryId, habilitada)` |

DTOs:

- Reaproveitados do `/acesso`: `MembroDoPerfilDto`, `PaginaDeMembros`, `UsuarioDto`, `FederacaoDto`.
- Próprios, onde o do `/acesso` carrega convenção da Intranet ou falta dado:
  - `PerfilDoTenantDto(Nome, TotalDePermissoes, Reservado)` e o detalhe correspondente, com a lista de
    permissões: sem a classificação setor/produto/comum.
  - `UsuarioDoTenantDetalheDto`: os campos do `UsuarioDetalheDto` mais `TemSenha` e `EntraPorSenha`,
    usados pela regra do reenvio de convite.

### Núcleo compartilhado

`OperacoesDeAcessoNoSecureGate` (internal, Infrastructure) concentra a chamada ao `ISecureGateClient`, o
mapeamento para DTO e a tradução de `ApiException`, `HttpRequestException` e timeout em erro de negócio,
sempre com `tenantId` explícito. O `SecureGateGestaoDeAcesso` atual passa a delegar a ele com o tenant da
requisição, **sem mudar comportamento**: a suíte atual do `/acesso` é a rede de segurança do refactor.
O `SecureGateGestaoDeAcessoDoTenant` delega com o tenant recebido.

Sem a seção `Secco:SecureGate`, `GestaoDeAcessoDoTenantIndisponivel` responde "não configurado", como os
outros adaptadores no-op.

## Regras dos handlers

Checadas na Application, antes da porta, e devolvidas como `Result`:

1. **Perfis reservados** da plataforma (`ClassificacaoDePerfil.EhReservado`) não aparecem nos seletores e
   não são criados, excluídos nem atribuídos. A plataforma recusa de qualquer jeito; a regra evita a
   chamada.
2. **Nome de perfil:** `ClassificacaoDePerfil.NomeValido`, até 100 caracteres. A convenção de sufixo de
   setor não se aplica.
3. **Excluir perfil com membros** é recusado. O 409 de perfil usado por client do tenant ganha mensagem
   própria.
4. **Cadastro:** e-mail obrigatório e válido. "Só pelo Entra" só é aceito com a federação do tenant
   habilitada, conferido no handler (POST forjado também é recusado), não só escondido na tela. E-mail
   que já existe no tenant tem mensagem própria.
5. **Reenviar convite** só para conta sem senha e que entra por senha (`TemSenha = false`,
   `EntraPorSenha = true`). A recusa da plataforma também é traduzida.
6. **Sem as regras da Intranet:** não há "último `intranet-admin`" nem a regra de não mexer na própria
   conta. O `intranet-admin` não é usuário daquele tenant; uma conta com o mesmo e-mail lá é outra conta.

### Permissões

Enquanto o catálogo da ADR-0039 não for publicado, a edição é o paliativo registrado na #33:

- lista em texto, **uma permissão por linha**, preenchida com as atuais do perfil;
- linhas vazias ignoradas e duplicadas descartadas;
- cada linha validada por `SeccoPermissions.IsValid`. Havendo inválida, nada é gravado, e o erro aponta
  as linhas;
- no máximo 200 permissões por perfil;
- gravação por substituição da lista inteira (`DefinirPermissoesDoPerfilAsync`).

**Tarefa prevista:** quando o `Secco.SecureGate.Client` com o catálogo sair, a caixa de texto vira lista de
caixas agrupada por produto, com as órfãs à parte, como o AdminPortal da ADR-0039. Porta e handler não
mudam; muda a tela e entra a leitura do catálogo.

## Autorização

- Cadeia em toda rota, nesta ordem: `[SomenteIntranetAdmin]`, `[ExigeSegundoFator]` e
  `[TenantAdministrado]` (os três do subsistema 1), e o filtro novo **`[SecureGateLigado]`**.
- `[SecureGateLigado]`: tenant do cadastro com `SecureGateHabilitado = false` responde **404**, a mesma
  regra de não confirmar o que existe.
- O `tenantId` vem **só da rota**, depois dos filtros. Nenhum formulário carrega tenant no corpo, e o
  controller não faz binding de tenant vindo de form ou query.
- Todo POST com `[ValidateAntiForgeryToken]`.

## Segurança

**🟠 Alto: tenant pela URL (IDOR).** Mitigado pelos filtros e pelo `tenantId` só de rota (ver
"Autorização").

**🟠 Alto: usuário ou perfil de outro tenant.** Um `usuarioId` do tenant da Intranet na rota do Compras
poderia atribuir perfil a conta alheia. A plataforma barra (`BelongsToTenantAsync`). A fumaça prova o caso,
para que uma regressão da plataforma apareça aqui.

**🟠 Alto: federação apontando para o diretório errado.** Trocar o directory id muda de qual Entra as
pessoas entram. Limitado pelo fail-closed: só casa quem já foi cadastrado, por e-mail, dentro daquele
`tid`. A tela mostra o directory id atual e **pede confirmação ao trocar um que já estava preenchido**.

**🟡 Médio: permissões em texto livre.** Formato validado, limite de 200, e o texto nunca é interpolado em
log, só a contagem.

**🟡 Médio: tenant desativado.** A gestão continua possível (preparar acessos antes de ativar é legítimo).
O cabeçalho mostra a situação para ninguém achar que o sistema está no ar.

**Resíduo aceito:** o raio de dano do `intranet-admin` passa a incluir conceder perfis em todos os sistemas
administrados, como a spec do subsistema 1 já registrou, com o 2FA como mitigação e a ressalva do login
federado.

## Telas

Só parciais do contrato de tema (ADR-0004), nos dois temas oficiais, claro e escuro.

| Rota | Conteúdo |
|---|---|
| `GET /tenants/{tenantId}/acesso` | Abas Perfis, Usuários e Federação. Cabeçalho com sistema, nome e situação do tenant, para ficar sempre claro **em qual sistema** se está mexendo. Ações Novo perfil e Cadastrar pessoa |
| `GET …/acesso/perfis/{nome}` | Membros paginados e formulário de permissões |
| `GET …/acesso/usuarios/{usuarioId}` | Situação, perfis com atribuir e retirar, ações de conta, reenviar convite quando couber |
| `GET/POST …/acesso/usuarios/novo` | E-mail, nome, perfis iniciais (caixas com os perfis do tenant) e modo de entrada; "só pelo Entra" só aparece com a federação habilitada |
| `POST`s | Um por ação, redirect com feedback, no padrão do `/acesso` |

O link de entrada fica na página do tenant (`/tenants/{tenantId}`), no painel do recurso SecureGate, e só
aparece quando ele está ligado.

## Auditoria

Na trilha do LogStream **da própria Intranet** (ADR-0006), falha-aberta, com o ator humano. Não vai para
o LogStream do sistema administrado: o recurso pode nem estar ligado lá, e quem age é usuário da Intranet.

| Verbo | Alvo |
|---|---|
| `tenant.acesso.perfil-criar` / `tenant.acesso.perfil-excluir` | tenant, perfil |
| `tenant.acesso.perfil-permissoes-editar` | tenant, perfil, quantidade antes e depois. **Nunca** a lista |
| `tenant.acesso.perfil-atribuir` / `tenant.acesso.perfil-retirar` | tenant, usuário, perfil |
| `tenant.acesso.usuario-cadastrar` | tenant, usuário, modo de entrada, perfis iniciais |
| `tenant.acesso.convite-reenviar` | tenant, usuário |
| `tenant.acesso.usuario-desativar` / `tenant.acesso.usuario-reativar` / `tenant.acesso.sessoes-encerrar` | tenant, usuário |
| `tenant.acesso.federacao-definir` | tenant, directory id, habilitada |

O e-mail do cadastrado não entra na trilha, só o id devolvido pela plataforma: é dado pessoal de terceiro
sem necessidade ali.

## Testes

- **Autorização:** o trio da ADR-0008 em todas as rotas novas; `intranet-admin` sem 2FA bloqueado;
  SecureGate indisponível fecha a área.
- **IDOR:** tenant fora do cadastro e tenant com SecureGate desligado dão 404 em todas as rotas; `tenantId`
  no corpo do POST é ignorado em favor do da rota.
- **Regras (unidade, fake da porta):** perfil reservado recusado; nome inválido; exclusão de perfil com
  membros; "só pelo Entra" sem federação habilitada recusado por POST direto; permissão inválida aponta a
  linha; duplicadas descartadas; limite de 200; reenvio de convite só para quem cabe.
- **Trilha:** cada escrita grava o verbo e o alvo esperados; nenhum e-mail e nenhuma lista de permissões no
  alvo.
- **Refactor do núcleo:** a suíte atual do `/acesso` passa sem mudança.
- **Adaptador:** cada método chama o client com o `tenantId` recebido, nunca o da requisição, com os dois
  diferentes no teste.
- **Fumaça opt-in contra o SecureGate real**, no padrão da fumaça de tenants: num tenant administrado,
  criar perfil, cadastrar pessoa nos dois modos, atribuir e retirar perfil, **atribuir com `usuarioId` de
  outro tenant e receber recusa**, editar permissões, desativar.
- **Telas:** as páginas novas renderizam nos dois temas.

## Fora do subsistema, registrado

- **A Intranet publicar o próprio catálogo de permissões.** Consequência da ADR-0039 para a Intranet em
  si, não deste subsistema: ao adotar o client com o catálogo, o `migrate` da Intranet publica
  `IntranetPermissoes`, e o client de máquina ganha o escopo `permissions:intranet` em
  `SecureGate:PlatformClients`. Sem isso, `GarantirPermissoes` dos setores passa a falhar para permissão
  nova. Entra como tarefa própria quando a plataforma publicar.

## Documentação a atualizar junto

- `docs/roadmap.md`: o item 2 dos subsistemas da Área administrativa aponta para esta spec.
- `docs/guia-de-deploy.md`: nada novo de configuração; os escopos do client de máquina não mudam.
