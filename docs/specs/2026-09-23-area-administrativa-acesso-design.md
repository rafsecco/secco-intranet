# Área administrativa de acesso — desenho do recurso

**Data:** 2026-09-23
**Estado:** rascunho, aguardando revisão

## Problema

O Inventário nasceu com a autorização certa (`intranet-admin` OU `inventario-admin`) e sem
nenhum jeito de **conceder** `inventario-admin` a alguém: a plataforma não sabia atribuir um
perfil a um usuário que já existia ([secco-platform#26](https://github.com/rafsecco/secco-platform/issues/26)).
O mesmo vale para os `{slug}-admin`/`{slug}-user` de cada setor — a Intranet sempre soube criar
a Role, e nunca soube dizer quem a tem. Quem administrava acesso precisava sair para o
SecureGate ou para o AdminPortal.

A #26 foi fechada. O `Secco.SecureGate.Client` 0.7.0+ entrega o que faltava, e a ADR-0008 já
decidiu **onde** isso mora: numa área administrativa da própria Intranet, exclusiva do
`intranet-admin`. Este documento desenha o primeiro corte dela.

## O que o levantamento apurou

Verificado no `secco-platform`, não presumido:

| Pergunta | Resposta |
|---|---|
| "Perfil" e "role" são a mesma coisa? | **Sim.** Perfil é o nome que a plataforma dá ao role nas telas e nas mensagens; não existe um agrupamento intermediário de roles |
| Dá para atribuir e retirar perfil de usuário existente? | Sim — `AddUserRoleAsync` e `RemoveUserRoleAsync`, ambos idempotentes; valem no token na próxima renovação |
| Dá para ver quem está num perfil? | Sim — `ListRoleMembersAsync`, paginado, com a situação da conta de cada um |
| O que traz o detalhe do perfil? | `GetRoleAsync`: permissões efetivas, se é reservado, quantos membros tem |
| O que traz o detalhe do usuário? | `GetUserAsync`: situação, perfis, permissões efetivas, logins externos, `twoFactorEnabled`, `hasPassword`, `localLoginEnabled` |
| Existe nome de exibição? | **Não.** `UserDto` e `UserDetailDto` têm só `Id`, `Email` e `Status` (`Active` \| `Deactivated` \| `LockedOut`) — o e-mail é a identidade exibível |
| `ListUsersAsync` pagina? | **Não.** Devolve todos os usuários do tenant; busca e paginação da tela acontecem em memória |
| Excluir perfil | `DeleteRoleAsync` recusa perfil com membros e perfil reservado |
| O que a plataforma **não** protege | `AddUserRoleAsync` aceita `installation-operator` (único reservado atribuível a usuário) e não conhece `intranet-admin`: tirar o último `intranet-admin` é permitido |
| Encerrar sessão | `RevokeUserSessionsAsync`; efeito nos produtos em até um TTL de cache (ADR-0032, adotada na Intranet em `fd63048`) |
| Criar usuário mudou | `CreateUser` perdeu `password` e ganhou `localLogin`: o admin não define senha, a plataforma envia convite. **Fora deste corte** |

## Decisões

| Eixo | Decisão |
|---|---|
| Escopo | Perfis e usuários do **próprio tenant**. Criar/convidar usuário e administrar outros tenants ficam para specs seguintes |
| Permissões de um perfil | **Só leitura neste corte.** O produto hoje checa o nome da Role, não permissão (`recurso:acao`, ADR-0021 da plataforma). Gravar permissão que nada consome faria o admin acreditar que um perfil como `all-user` funciona quando não funciona. Editar permissões nasce junto com a autorização por permissão, na spec seguinte (ver "Fora de escopo") |
| Quem acessa | **Somente `intranet-admin`**, sem o OR do Inventário (ADR-0008). `{slug}-admin` e `inventario-admin` não entram |
| Onde moram as regras | Handlers na Application, sobre uma porta `IGestaoDeAcesso` (ADR-0002); o controller só orquestra |
| Falha do SecureGate | **Não é falha aberta.** Diferente da auditoria, o admin precisa saber que a operação não aconteceu: vira `Result` com mensagem sem detalhe interno |
| Sem SecureGate configurado | A área abre e mostra "SecureGate não configurado" — modo aberto de DEV e ambiente Testing não quebram |
| Perfil reservado | Nunca atribuível pela tela, mesmo que a plataforma aceite |
| Rastro | Toda escrita vai ao LogStream, falha-aberta como o resto da auditoria |
| Setores | `SetoresController` passa a exigir `intranet-admin` (ver seção própria) |

## Modelo de acesso que a tela precisa expressar

Regra de negócio confirmada em 2026-09-24:

- **Só o `intranet-admin` cria perfis e atribui perfis.** Nenhum admin de setor concede
  acesso, nem `{slug}-user`, nem `{slug}-admin`, nem no próprio setor. Não existe tela de
  "Membros" dentro do setor.
- **`{slug}-admin` altera o próprio setor** (Mural, Documentos, o que o setor publicar);
  **`{slug}-user` só lê.** É o que Mural e Documentos já fazem; esta área não muda isso.
- **A relação usuário ↔ setor é muitos-para-muitos, nos dois papéis.** Um usuário pode ser
  `{a}-user` de vários setores, e também `{a}-admin` de mais de um. Ser admin de um setor não
  implica pertencer a outro, e ser `{slug}-admin` não implica ser `{slug}-user` do mesmo setor
  — são perfis independentes, como a ADR-0001 já define.
- **`intranet-admin` tem acesso a tudo, sempre** — é o mestre da instalação.

Consequência para a interface: como um mesmo usuário acumula vários perfis de setor, o detalhe
do usuário **agrupa os perfis por setor** (setor, papel), em vez de uma lista plana de nomes
de role, e o seletor de "Adicionar perfil" oferece o setor e o papel como duas escolhas
separadas. O detalhe do perfil (a visão inversa) segue listando os membros.

## Arquitetura

```text
Web ──► handlers (Application/Acesso) ──► IGestaoDeAcesso ──► SecureGateGestaoDeAcesso ──► ISecureGateClient
            │                                                     └─ tenant: ITenantContext
            └──► ITrilhaDeAuditoria
```

`IGestaoDeAcesso` (Application) devolve DTOs próprios — `PerfilDto`, `PerfilDetalheDto`,
`MembroDoPerfilDto`, `UsuarioDto`, `UsuarioDetalheDto` — e nunca tipos do client gerado, para a
borda HTTP e os handlers não dependerem do contrato do SecureGate. Operações:

| Leitura | Escrita |
|---|---|
| `ListarPerfisAsync` | `CriarPerfilAsync(nome)` |
| `ObterPerfilAsync(nome)` | `ExcluirPerfilAsync(nome)` |
| `ListarMembrosAsync(nome, página)` | `AtribuirPerfilAsync(usuarioId, perfil)` |
| `ListarUsuariosAsync` | `RetirarPerfilAsync(usuarioId, perfil)` |
| `ObterUsuarioAsync(usuarioId)` | `DesativarUsuarioAsync` / `ReativarUsuarioAsync` |
| | `EncerrarSessoesAsync(usuarioId)` |

O adaptador `SecureGateGestaoDeAcesso` mapeia `ApiException`, `HttpRequestException` e o timeout
do `HttpClient` (`OperationCanceledException` sem cancelamento do chamador) para erros de
negócio — o mesmo cuidado dos três adaptadores anteriores, e pelo mesmo motivo: o timeout
escapando derrubaria a resposta. `409` da plataforma (autodesativação, último operador) e `404`
têm mensagem própria; nada de detalhe interno na tela.

Sem SecureGate configurado, `GestaoDeAcessoIndisponivel` (no-op, a seleção por configuração que
o produto já usa três vezes) responde "não configurado" e a tela renderiza o estado vazio.

## Regras dos handlers

Checadas **antes** de chamar a porta, e devolvidas como `Result` (ADR-0004):

1. **Perfil reservado não é atribuível.** Vale para atribuir e para o seletor da tela.
2. **`intranet-admin` nunca fica sem membro ativo.** Retirar de quem é o último membro ativo é
   recusado. A contagem sai de `ListarMembrosAsync`.
3. **Ninguém tira `intranet-admin` de si mesmo nem se desativa.** Fazer isso trancaria a própria
   pessoa para fora da área; só outro `intranet-admin` pode. A plataforma já responde `409` para
   a autodesativação — a Intranet checa antes, para a mensagem ser dela.
4. **Perfil protegido não é excluído:** `intranet-admin`, `inventario-admin` e qualquer perfil
   de nome `{x}-admin`/`{x}-user`, por convenção de sufixo — não consulta a tabela de setores:
   é o lado seguro do erro (apagar o de um setor quebraria o vínculo), e um perfil comum que
   termine assim também fica protegido.
5. **Perfis do produto:** `intranet-admin` e `inventario-admin` que ainda não existem no tenant
   aparecem numa seção própria com botão **Criar**. É o que faz o `inventario-admin` passar a
   poder existir, e o que fecha o item que o Inventário deixou em aberto.

> O **primeiro** `intranet-admin` continua vindo de fora: alguém precisa criar o perfil e
> atribuí-lo pelo AdminPortal/SecureGate, porque a área só abre para quem já o tem. Fica
> registrado no README como passo de instalação — não é falha do desenho, é o ovo e a galinha
> de qualquer console de administração.

## Autorização

`AcessoAdministrativo` ganha `SomenteIntranetAdmin(ClaimsPrincipal?)`: true só com a Role
`intranet-admin`, sem aceitar Role específica. É o oposto deliberado de `TemAcesso`, que existe
para recursos delegáveis; esta área não é delegável (ADR-0008).

Toda action é coberta por `[SomenteIntranetAdmin]` na classe — um filtro de autorização com
`Order` menor que o do antiforgery, que devolve 403; o bypass de "modo aberto" segue a mesma
condição do Inventário (`environment.IsDevelopment() && !IsConfigured(configuration)`), nunca no
ambiente `Testing`. O filtro substitui o `PodeAdministrar()` por action: nenhuma action nova
nasce desprotegida, e o `POST` de quem não é admin dá 403 mesmo sem token.

O item de menu "Acesso" aparece só para `intranet-admin`, dentro do grupo "Administração".
Hoje esse grupo só contém "Setores" e aparece para qualquer `{slug}-admin`
(`SetorAcesso.AdministraAlgumSetor`); passa a aparecer só para `intranet-admin`, com dois
itens: Setores e Acesso.

## Setores

`SetoresController` (criar, editar, listar setores) não tem hoje nenhuma checagem: qualquer
usuário autenticado que digite `/Setores/Create` cria um setor — e, com ele, duas Roles no
SecureGate. Entra aqui porque é a mesma área e a mesma regra da ADR-0008. Passa a exigir
`intranet-admin` em toda action, com os mesmos testes negativos; o menu segue a regra da
seção anterior.

**Consequência a registrar:** um `{slug}-admin` deixa de conseguir editar o próprio setor
por `/Setores`. O que ele administra dentro do setor (Documentos, Avisos) segue nas rotas
`/setor/{slug}`, que já têm o guarda `{slug}-admin`. A edição do setor em si é decisão de
instalação, e o admin da instalação é o `intranet-admin`.

Entregue: `SetoresController` sob `[SomenteIntranetAdmin]`; o grupo "Administração" do menu só
aparece para o `intranet-admin`.

## Auditoria

Verbos novos em `VerbosDeAuditoria`, recurso `acesso`:

| Verbo | Metadata (sem dado sensível) |
|---|---|
| `acesso.perfil-criar` / `perfil-excluir` | nome do perfil |
| `acesso.perfil-atribuir` / `perfil-retirar` | perfil, id e e-mail do usuário afetado |
| `acesso.usuario-desativar` / `usuario-reativar` | id e e-mail do usuário afetado |
| `acesso.sessoes-encerrar` | id e e-mail do usuário afetado |

O ator é o `intranet-admin` que agiu (mesmo `IAtorAtual` de Mural/Documentos/Setor). Leitura
não é auditada, como no resto do produto (ADR do rastro: volume).

## Telas

Views no core, usando só os partials do contrato de tema — nenhum arquivo em `Themes/*`.

- **`/acesso`** — duas abas. **Perfis:** lista com nome, nº de **permissões** (a `ListRoles` da plataforma não traz a contagem de
  membros, e uma chamada por perfil seria N+1; os membros ficam no detalhe), badge "reservado"/
  "do setor"/"do produto"; seção "Perfis do produto" com **Criar** para os inexistentes; criar
  perfil livre. O nome segue a regra da plataforma: letras, dígitos, `.`, `_` e `-`, sem
  espaço, até 100 caracteres; a tela valida antes de enviar e mostra o formato esperado
  (`equipe-financeiro`, não "Equipe Financeiro"), porque o SecureGate não guarda nome de
  exibição para perfil. **Usuários:** lista com e-mail, situação e perfis; busca por e-mail e paginação
  em memória (a API não pagina).
- **Detalhe do perfil** — permissões efetivas **somente leitura**; membros paginados com
  situação da conta e **Retirar**; **Adicionar membro** por seletor de usuários que ainda não
  são membros; **Excluir** quando permitido.
- **Detalhe do usuário** — situação; perfis com **Retirar**; **Adicionar perfil** por seletor
  só de perfis atribuíveis; **Desativar/Reativar**; **Encerrar sessões**; 2FA e logins externos
  só leitura (sem reset de 2FA neste corte — a plataforma o audita e avisa o dono, e a decisão
  de expor é separada).

Cada botão que muda estado é um `POST` com token antifalsificação e confirmação em
`FeedbackViewComponent` no retorno. Limitação conhecida e já registrada no Inventário: o
componente só produz `ToastVariante.Sucesso`, então uma recusa de regra aparece no mesmo toast
verde. Os dois temas **já** renderizam `ToastVariante.Erro` (`sc-toast--erro`); falta só o
produtor no core — uma segunda chave de `TempData` lida por `FeedbackViewComponent`. Esta
feature é o primeiro grande produtor de recusa (último admin, autoexclusão, perfil protegido),
então entra no escopo, e o `InventarioController` passa a usá-la no lugar do texto de erro no
toast verde.

## Testes

- **Unitários das regras** com uma `IGestaoDeAcesso` falsa: reservado não atribui; último
  `intranet-admin` não sai; autoatingir-se é recusado; perfil protegido não é excluído; criação
  de perfil do produto só para os dois nomes conhecidos.
- **Adaptador** com subclasse do `SecureGateClient` gerado (não um fake da interface inteira —
  ela cresce a cada release e o fake quebra a cada bump): timeout, `ApiException` 404/409 e
  `HttpRequestException` viram `Result`, nunca exceção.
- **Integração — matriz de autorização em toda rota**, GET e POST:

  | Usuário | Esperado |
  |---|---|
  | sem role | 403 |
  | `{slug}-admin` | 403 |
  | `inventario-admin` | 403 |
  | `intranet-admin` | 200 / redireciona |

  Nos `POST`, o token antifalsificação é obtido por um cliente `intranet-admin` e **enviado por
  um cliente sem a role** — assim o teste prova o 403 da autorização, e não o 400 do filtro de
  antiforgery que barra antes (limitação do teste do Inventário).
- **Setores:** a mesma matriz para `/Setores`, `/Setores/Create`, `/Setores/Edit/{id}`.
- **Efeito real:** o adaptador é exercitado contra o client gerado com `HttpMessageHandler`
  falso, conferindo método, rota e `tenantId` de cada chamada.

## Fora de escopo

- **Criar/convidar usuário, reenviar convite, redefinir senha** — spec própria; dependem do
  e-mail configurado no SecureGate.
- **Outros tenants e provisionamento de banco** — o restante da ADR-0008, spec própria.
- **Modelo de permissões e edição das permissões de um perfil** — spec própria, logo depois
  desta. Objetivo confirmado em 2026-09-24: o `intranet-admin` cria um perfil (ex.: `all-user`)
  e atribui várias permissões a ele, de modo que quem pertence a esse perfil leia todos os
  setores, ou o que mais for atribuído, sem receber um `{slug}-user` por setor. O perfil é
  o "agrupamento": o usuário recebe **um** perfil no lugar de vários. Perfil que contém outros
  perfis (aninhamento) não existe na plataforma e não é necessário, porque agrupar permissões
  no perfil já resolve. A plataforma
  já entrega a base (permissões `recurso:acao`, `SetRolePermissionsAsync`, resolução em
  runtime com cache, revogação imediata); o que falta é o produto **autorizar por permissão**
  em vez de por nome de Role. Essa spec cobre: catálogo de permissões definido pelo produto
  (a tela oferece uma lista, não texto livre); permissão global além da por setor, porque o
  formato da plataforma não aceita curinga e "todos os setores, inclusive os futuros" precisa
  dela; migração de Mural, Documentos, Inventário e menu; os itens de menu por setor abaixo,
  em que cada item vira uma permissão; e a relação com a ADR-0001 (`{slug}-admin`/`{slug}-user`
  passam a ser perfis que já nascem com as permissões do setor). Esta spec já prepara o
  terreno: o detalhe do perfil mostra as permissões, e criar perfil é livre.
- **Reset de segundo fator e remoção de login externo** — a plataforma os audita e avisa o dono;
  expor é decisão de segurança separada.
- **Itens de menu por setor com permissão por item** (`ItemMenu`, Fase 2 do roadmap) — spec
  própria. A regra de quem pode já está fixada: só o `intranet-admin` cria itens e atribui a
  permissão de acesso de cada um; um setor terá vários itens. Este corte é pré-requisito:
  entrega os perfis e a atribuição de usuários que aquela permissão vai referenciar.
- **Grupos do AD/Entra como origem de perfis e de membros** — não existe na plataforma: a
  federação da ADR-0026 só prova identidade e o AD nunca concede acesso. São duas capacidades
  pedidas à plataforma, em duas issues: [#27](https://github.com/rafsecco/secco-platform/issues/27)
  (listar os grupos do diretório, para o `intranet-admin` escolher quais viram perfis) e
  [#28](https://github.com/rafsecco/secco-platform/issues/28) (mapeamento grupo → perfil aplicado
  no login, opt-in, exige ADR nova). Quando existirem, esta área ganha a escolha de grupos e a
  marcação de "atribuição vinda do diretório" (o admin não deve remover à mão o que volta no
  próximo login); até lá, criar perfil e atribuir usuário é manual e não muda o desenho.
- **"Acessar como"** — decisão de segurança independente (ADR-0008 e ADR-0030): só o
  `intranet-admin`, nunca outro admin, com a mesma matriz de testes negativos quando existir.
- **Federação Entra ID** — a plataforma já cadastra por tenant (`PUT /tenants/{id}/federation`);
  ligar isso na Intranet é a Fase 4 do roadmap.

## Documentação a atualizar junto

- Spec do Inventário: "Fora de escopo" perde a tela de conceder acesso (agora é esta).
- `docs/roadmap.md`: item "Área administrativa" ganha o primeiro corte entregue; "Tela de
  administração de setores" registra a proteção; Inventário deixa de citar a #26 como bloqueio.
- `docs/plataforma.md`: linha da #26 passa para "Atendidas" (Client 0.7.0, 2026-09-17).
- README: passo de instalação do primeiro `intranet-admin`.
