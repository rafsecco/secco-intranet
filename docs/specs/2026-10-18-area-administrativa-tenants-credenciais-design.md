# Área administrativa de tenants — subsistema 4: credenciais do sistema administrado

**Data:** 2026-10-18
**Estado:** implementado

## Problema

O subsistema 1 ([spec](2026-10-07-area-administrativa-tenants-design.md)) cria o tenant do "Sistema de
compras" e liga os recursos da plataforma. O subsistema 2
([spec](2026-10-11-area-administrativa-tenants-acesso-design.md)) cuida dos perfis e das pessoas desse
tenant. Ainda falta o próprio sistema conseguir falar com a plataforma. Hoje não há como o Compras:

- gravar log no LogStream ou mandar notificação pelo Hub, porque não tem credencial de máquina;
- deixar os funcionários entrarem nele pelo SSO da plataforma, porque não tem client de login vinculado ao
  tenant dele (ADR-0040 da plataforma, consequência registrada na ADR-0009 daqui).

Este subsistema dá ao `intranet-admin` a emissão, a alteração, a rotação do secret e a revogação dessas
credenciais pela Intranet. O secret é exibido uma única vez, e quem administra o entrega ao time do
sistema.

## O que a plataforma oferece

Verificado no `secco-platform` (ADR-0037 e ADR-0040, `Secco.SecureGate.Client` 0.19.0), não presumido:

| Pergunta | Resposta |
|---|---|
| API | `/api/v1/tenants/{tenantId}/clients`, sob `securegate:admin`: `POST` cria e devolve o secret, `GET` lista, `GET /{clientId}` detalha, `PUT /{clientId}` substitui nome, escopos, papéis e URIs (idempotente), `POST /{clientId}/rotate-secret` devolve o secret novo, `DELETE /{clientId}` revoga |
| Admin de tenant (ADR-0044) pode usar? | **Não**: clients de tenant ficam só com o operador. A Intranet usa o client de máquina, como na criação de tenant ([secco-platform#39](https://github.com/rafsecco/secco-platform/issues/39)) |
| Tipos | `client_credentials` (padrão, máquina) e `authorization_code` (login de pessoas: confidencial, PKCE obrigatório, exige redirect URI). O tipo não muda depois de criado (`TypeImmutable`) |
| `clientId` e secret | Gerados pelo servidor. `cli_` + 16 caracteres base32 minúsculos; o secret tem 32 bytes em base64url. Aparece só na criação e na rotação, e é guardado só como hash |
| Escopos de produto | Lista fechada no código: `logstream` e `notificationhub`. Os de infraestrutura são recusados (`ScopeNotAllowed`). O client interativo recebe também os OIDC padrão (`openid`, `profile`, `email`, `roles`, `offline_access`) |
| Papéis | Perfis existentes do tenant. Reservado recusado (`RoleNotAssignable`), inexistente recusado (`RoleNotFound`). **Client interativo não aceita papéis** (`RolesNotAllowedForInteractive`): os perfis são das pessoas |
| Limites | Nome 1–100 sem caractere de controle, único no tenant (`NameAlreadyExists`); até 10 escopos, 20 papéis e 10 redirect URIs (e 10 de logout), cada uma com até 2000 caracteres |
| Redirect URI | Absoluta, http(s), sem credencial nem fragmento; HTTPS obrigatório fora de Development **do SecureGate** |
| Token | O de máquina sai com `tenant_id` do vínculo. Tenant desativado não recebe token (`TenantInactive` na criação) |
| Rotação e revogação | Imediatas. Tokens já emitidos valem até expirar (TTL do access token, 5 min por padrão) |
| Excluir perfil usado por client | `409 SecureGate.Role.UsedByClients` (o subsistema 2 já traduz) |

## Escopo

| Operação | Observação |
|---|---|
| Listar as credenciais do tenant | Nome, tipo, `clientId`, escopos, perfis (máquina) ou URIs (login), data |
| Emitir | Tipo, nome, escopos e, conforme o tipo, perfis ou redirect URIs |
| Alterar | Nome, escopos e perfis ou URIs. O tipo não muda |
| Rotacionar o secret | Com confirmação; o secret novo aparece uma vez |
| Revogar | Com confirmação digitando o nome da credencial |

### Fora de escopo

- Client público (SPA, mobile): a plataforma não oferece.
- Rotação com sobreposição de secrets: a plataforma recusou (ADR-0037).
- Client de sistema que é ele mesmo resource server ou multi-tenant: pela ADR-0037 é serviço de plataforma
  e entra pela configuração do operador, não por aqui.
- Guardar o secret na Intranet para "mostrar de novo": nunca. Perdeu, rotaciona.

## Decisões

| Eixo | Decisão |
|---|---|
| Rota | `/tenants/{tenantId}/credenciais`, link no painel da página do tenant |
| Exige SecureGate ligado? | **Não para a área**: um Compras que só usa o LogStream precisa da credencial de máquina para gravar log. **Sim para o tipo login** e para atribuir perfis: sem SecureGate ligado, o tenant não tem pessoas nem perfis administrados pela Intranet. A regra fica no handler, não só na tela |
| Escopos oferecidos | Só os dos recursos **ligados** no tenant (status de banco da plataforma, como na página do tenant): `logstream` se o LogStream está ligado, `notificationhub` se o Hub está. Escopo de recurso desligado é recusado no handler. O texto do formulário é mapeado de uma lista fechada e nunca vai cru para a plataforma |
| Perfis oferecidos (máquina) | Os perfis do tenant, sem os reservados, lidos pela porta do subsistema 2 |
| Redirect URI | Uma por linha, até 10. Validada na Intranet com a mesma regra da plataforma, exigindo HTTPS. `http://` só passa com a Intranet em Development. A plataforma confere de novo pelo ambiente dela |
| Nome | Obrigatório, aparado, 1–100 sem caractere de controle |
| Exibição do secret | Na resposta do próprio POST (emitir e rotacionar), sem redirect, com `Cache-Control: no-store`. Nunca em TempData, cookie, banco, log ou trilha. Mesmo regime do script de provisionamento do subsistema 1 |
| O que acompanha o secret | O `clientId`, a Authority do SecureGate e um bloco de variáveis de ambiente prontas para o sistema (`Secco__SecureGate__Authority`, `__ClientId`, `__ClientSecret`), com o aviso de que não há como ver o secret de novo |
| `{clientId}` da rota | Precisa casar com `^cli_[a-z2-7]{16}$`. Fora disso é **404** sem chamar a plataforma |
| Revogar | Confirmação digitando o nome da credencial, como a desativação de tenant pede o slug |
| Rotacionar | Confirmação simples, com o aviso de que o sistema para de obter token até receber o secret novo |
| Tenant desativado | A gestão continua possível. A plataforma recusa emitir para tenant desativado (`TenantInactive`), e a tela traduz |
| Conflito de nome | Mensagem própria |

## Arquitetura

O mesmo molde do subsistema 2: uma porta própria, com o tenant explícito em toda assinatura.

```text
Web: TenantsCredenciaisController  /tenants/{tenantId}/credenciais/...
  [SomenteIntranetAdmin] → [ExigeSegundoFator] → [TenantAdministrado]
        │
Application/Tenants/Credenciais: handlers ──► IGestaoDeCredenciaisDoTenant (tenantId explícito)
        │                                  ├► IGestaoDeTenants (status dos recursos ligados)
        │                                  ├► IGestaoDeAcessoDoTenant (perfis do tenant)
        │                                  └► ITrilhaDeAuditoria
Infrastructure/Access:
  SecureGateGestaoDeCredenciaisDoTenant ─► ISecureGateClient (client de máquina, securegate:admin)
  GestaoDeCredenciaisDoTenantIndisponivel  (sem SecureGate configurado)
```

### Porta `IGestaoDeCredenciaisDoTenant`

Na Application (`Tenants/Credenciais`). Todo método recebe `Guid tenantId` primeiro e devolve `Result`.

| Leitura | Escrita |
|---|---|
| `ListarAsync(tenantId)` | `EmitirAsync(tenantId, dados)` → `CredencialEmitidaDto` (com secret) |
| `ObterAsync(tenantId, clientId)` | `AlterarAsync(tenantId, clientId, dados)` |
| | `RotacionarSecretAsync(tenantId, clientId)` → `SecretDaCredencialDto` |
| | `RevogarAsync(tenantId, clientId)` |

DTOs próprios, nunca tipos do client gerado:

- `TipoDeCredencial { Maquina, Login }`. Mapeado de `client_credentials` (ou tipo nulo) e
  `authorization_code`. Tipo desconhecido vindo da plataforma vira erro de indisponibilidade, e a
  credencial não é mostrada como de outro tipo.
- `CredencialDto(ClientId, Nome, Tipo, Escopos, Perfis, RedirectUris, RedirectUrisDeLogout, CriadaEm)`.
- `DadosDaCredencial(Tipo, Nome, Escopos, Perfis, RedirectUris, RedirectUrisDeLogout)`. Na alteração,
  o tipo vem da credencial lida, nunca do formulário.
- `CredencialEmitidaDto(Credencial, Secret)` e `SecretDaCredencialDto(ClientId, Secret)`. O secret nunca
  aparece em `ToString()`: ele fica fora do record posicional ou em tipo que sobrescreve `ToString`.

O adaptador traduz `ApiException` pelo `ProblemDetails.Title`, como o `OperacoesDeAcessoNoSecureGate`
faz desde a correção do `TemCodigo`. Também traduz `HttpRequestException` e o timeout:

| Código da plataforma | Erro da Intranet |
|---|---|
| `SecureGate.Client.NotFound`, `SecureGate.Tenant.NotFound` | `Credenciais.NaoEncontrada` (404) |
| `SecureGate.Client.NameAlreadyExists` | `Credenciais.NomeEmUso` |
| `SecureGate.Client.TenantInactive` | `Credenciais.TenantDesativado` |
| `SecureGate.Client.RoleNotFound` / `RoleNotAssignable` | `Credenciais.PerfilRecusado` |
| `SecureGate.Client.RedirectUrisInvalid` / `PostLogoutRedirectUrisInvalid` | `Credenciais.EnderecoRecusado` |
| outros `400` de validação | `Credenciais.Recusada`, com o `Detail` da plataforma |
| rede, timeout, 5xx | `Acesso.Indisponivel` (o mesmo dos outros adaptadores) |

## Regras dos handlers

Checadas antes de chamar a porta, devolvidas como `Result`:

1. **Nome:** aparado, 1–100, sem caractere de controle.
2. **Escopos:** só os dos recursos ligados (o status de banco é lido a cada emissão e alteração). Pelo
   menos um escopo para máquina. O tipo login pode ficar só com os OIDC padrão, que a plataforma
   acrescenta sozinha.
3. **Perfis (máquina):** cada um precisa existir no tenant e não ser reservado
   (`ClassificacaoDePerfil.EhReservado`), até 20. Com SecureGate desligado, a lista precisa vir vazia.
4. **Login:** exige SecureGate ligado. Exige pelo menos uma redirect URI. Não aceita perfis. Redirect URIs
   e URIs de logout seguem a regra da tabela de decisões, até 10 de cada.
5. **Máquina:** não aceita redirect URI.
6. **Alterar:** o tipo vem da credencial lida. Formulário forjado com outro tipo é ignorado.
7. **Revogar:** o nome digitado precisa ser igual ao da credencial lida na hora.

## Autorização

- Cadeia em toda rota: `[SomenteIntranetAdmin]`, `[ExigeSegundoFator]`, `[TenantAdministrado]`. Os três
  do subsistema 1, sem `[SecureGateLigado]` na classe (ver Decisões).
- `tenantId` só da rota; `clientId` só da rota, validado pelo formato.
- Todo POST com `[ValidateAntiForgeryToken]`.

## Segurança

**🔴 Crítico: o secret.** Quem tem o secret age como o sistema no LogStream e no Hub, e com os perfis
atribuídos, dentro do tenant. Mitigações:

- exibição única, na resposta do POST, com `no-store`;
- nunca em TempData, cookie, banco, log ou trilha, provado por teste como no script de provisionamento;
- rotação imediata disponível na tela.

**Resíduo:** até 5 minutos de token vivo depois de rotacionar ou revogar (ADR-0037). A tela avisa.

**🟠 Alto: credencial em outro tenant (IDOR).** Um `clientId` do tenant A na rota do tenant B. A plataforma
responde 404 para client de outro tenant (ADR-0037). A Intranet confere o tenant pelo cadastro
(`[TenantAdministrado]`), e a fumaça prova a recusa da plataforma.

**🟠 Alto: redirect URI de terceiro no client de login.** Uma URI apontando para fora mandaria o code da
pessoa para lá. Mitigações:

- HTTPS obrigatório;
- PKCE obrigatório pela plataforma;
- a tela mostra as URIs atuais em destaque;
- a trilha registra cada alteração delas.

**Resíduo:** um `intranet-admin` comprometido consegue isso, e o 2FA da área é a mitigação, como no resto
da área.

**🟠 Alto: perfil poderoso numa máquina.** Dar ao client um perfil com escrita ampla é decisão de quem
administra. A tela mostra as permissões de cada perfil oferecido (contagem e link para o perfil).

**🟡 Médio: rotação derruba o sistema.** O secret antigo morre na hora. Por isso a confirmação vem com aviso
explícito.

**🟡 Médio: escopo arbitrário.** Os escopos vêm da lista fechada da Intranet, filtrada pelos recursos ligados.

## Telas

Só parciais do contrato de tema (ADR-0004), nos dois temas oficiais, claro e escuro.

| Rota | Conteúdo |
|---|---|
| `GET /tenants/{tenantId}/credenciais` | Cabeçalho do tenant (o `CabecalhoDoTenant` do subsistema 2). Tabela com as credenciais e ação Emitir |
| `GET /tenants/{tenantId}/credenciais/nova?tipo=maquina\|login` | Formulário pelo tipo. Escopos em caixas (só os recursos ligados), perfis em caixas (máquina), URIs em texto, uma por linha (login) |
| `POST …/credenciais/nova` | Sucesso: tela de exibição única do secret. Erro: o formulário com a mensagem |
| `GET …/credenciais/{clientId}` | Detalhe com o formulário de alteração, o botão Rotacionar e a área Revogar |
| `POST …/{clientId}` | Altera e redireciona para o detalhe com feedback |
| `POST …/{clientId}/rotacionar` | Exibição única do secret novo |
| `POST …/{clientId}/revogar` | Confirmação pelo nome. Redireciona para a lista |

Na página do tenant (`/tenants/{tenantId}`), um painel **Credenciais** com o link. Sem contagem: seria
mais uma chamada à plataforma a cada abertura da página, e uma falha nela não pode derrubar a página do
tenant.

## Auditoria

Na trilha do LogStream **da própria Intranet**, falha-aberta, com o ator humano. Na plataforma, o ator é o
client de máquina da Intranet. **Nunca o secret.**

| Verbo | Alvo |
|---|---|
| `tenant.credencial.emitir` | tenant, clientId, tipo, escopos, perfis, quantidade de redirect URIs |
| `tenant.credencial.alterar` | tenant, clientId, escopos, perfis e as redirect URIs (são públicas e são o ponto sensível do tipo login) |
| `tenant.credencial.rotacionar` | tenant, clientId |
| `tenant.credencial.revogar` | tenant, clientId, nome |

## Testes

- **Autorização:** o trio da ADR-0008 em todas as rotas; sem 2FA bloqueado; SecureGate indisponível fecha.
- **IDOR:** tenant fora do cadastro dá 404 em toda rota; `clientId` fora do formato dá 404 sem chamar a
  porta; `tenantId` no corpo é ignorado.
- **Segredo:**
  - o secret aparece só na resposta do POST de emitir e de rotacionar, com `no-store`;
  - não aparece na trilha, em log capturado, em TempData nem no redirect;
  - `ToString()` dos DTOs não traz o secret.
- **Regras (unidade, fakes das portas):**
  - escopo de recurso desligado recusado;
  - login com SecureGate desligado recusado por POST direto;
  - login sem redirect URI recusado;
  - login com perfis recusado;
  - máquina com redirect URI recusada;
  - perfil reservado e perfil inexistente recusados;
  - URI `http://` recusada fora de Development;
  - URI com fragmento ou credencial recusada;
  - limites;
  - tipo forjado na alteração ignorado;
  - revogação com nome errado recusada.
- **Adaptador:** cada método usa o `tenantId` recebido. A tradução de cada código de erro pelo `Title` do
  `ProblemDetails`, com `Response` vazio como no client real.
- **Fumaça opt-in contra o SecureGate real:**
  1. emitir uma credencial de máquina com `logstream` e obter token por `client_credentials`, com `tenant_id`
     do tenant;
  2. rotacionar e ver o secret antigo recusado;
  3. **detalhar o `clientId` pela rota de outro tenant e receber 404**;
  4. emitir uma de login com redirect HTTPS;
  5. revogar as duas.
- **Telas:** as páginas novas renderizam nos dois temas.

## Documentação a atualizar junto

- `docs/roadmap.md`: item 4 dos subsistemas aponta para esta spec.
- `docs/guia-de-deploy.md`: nada novo de configuração na Intranet. Uma nota de operação: o secret
  entregue ao time do sistema vai para o cofre dele, nunca para repositório.
- `docs/plataforma.md`: a linha da #39 cita também clients de tenant.
