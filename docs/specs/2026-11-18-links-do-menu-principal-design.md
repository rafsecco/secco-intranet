# Links do menu principal — design

**Estado:** entregue em 2026-11-18.

## Por quê

Toda empresa tem atalhos que valem para todo mundo e não pertencem a nenhum setor: abrir um chamado no service desk, o webmail, um portal terceirizado. Hoje o grupo principal do menu (Mural, Diretório, bibliotecas e extras) só aceita itens registrados em código (`IItemDeMenuPrincipal`), e o `CatalogoDeItensDoMenuPrincipal` recusa URL que não seja caminho local. Link externo com "abrir em nova aba" só existe dentro da árvore de um setor ([Links do setor](2026-11-15-links-do-setor-design.md)). A saída hoje é criar um setor artificial ou escrever uma RCL.

Esta spec cobre o que a de Links do setor deixou fora do escopo ("links da empresa inteira fora dos setores").

## Objetivos

- Quem tem `intranet-admin` cria, edita e remove links simples no grupo principal, pela tela **Administração → Menu** (`/menu`).
- O link aparece para **todo usuário autenticado do tenant**, nos temas Vertical e Horizontal, sem mudar nenhuma view de tema nem o contrato de tema.
- O link entra na **mesma ordenação por tenant** que `/menu` já faz, junto com Mural, Diretório, bibliotecas e extras.
- Criar, editar e remover passam pela **trilha de auditoria**.

## Fora do escopo

- Restringir o link por perfil, setor ou permissão.
- Submenu ou grupo de links no grupo principal: o link é folha.
- Links pessoais de cada usuário.
- Verificar se o endereço responde ou contar cliques.
- Mudar `IItemDeMenuPrincipal` ou os itens em código: continuam como estão.

## O que não pode acontecer

- Um link levar a `javascript:`, `data:`, `//host`, `/\host` ou qualquer esquema que não seja `http`/`https` ou caminho local.
- Um link abrir em nova aba sem `rel="noopener noreferrer"`.
- Um link aparecer em outro tenant.
- Alguém sem `intranet-admin` criar, editar ou remover link.
- Uma view de tema, ou o `NavigationItemModel`, precisar mudar.

## Como fica para quem usa

Menu de qualquer usuário (exemplo, depois de o admin reordenar):

```
Mural
Diretório
Abrir um chamado        ↗ (nova aba)
Políticas internas      (biblioteca)
Webmail                 ↗ (nova aba)
──────── Setores ────────
...
```

Tela `/menu` do admin, com um card novo **Links** acima dos cards de ordenação:

```
Links                                                   [ + Link ]
  🎧 Abrir um chamado   https://suporte.exemplo.com.br   nova aba      editar  excluir
  ✉ Webmail             https://mail.exemplo.com.br      nova aba      editar  excluir
  (vazio: "Nenhum link. Atalhos para sistemas que valem para a empresa toda.")

Principal                                (ordenação, como hoje; os links entram aqui)
  Mural                ↑ ↓
  Diretório            ↑ ↓
  Abrir um chamado     ↑ ↓
  ...
```

Formulário de criar e editar (página própria):

```
Rótulo *            [ Abrir um chamado                 ]
URL *               [ https://suporte.exemplo.com.br   ]
                    Caminho da intranet (/...) ou endereço http/https.
Ícone               [ bi-headset                       ]  (opcional)
[x] Abrir em nova aba
                                     [ Cancelar ] [ Salvar ]
```

Regras:

| Assunto | Regra |
|---|---|
| Rótulo | Obrigatório, aparado, até 256 caracteres |
| URL | Caminho local começando com `/` (e não com `//` nem `/\`) ou URL absoluta `http`/`https`; até 512 caracteres. Qualquer outra coisa é recusada com a mensagem no campo |
| Ícone | Opcional; classe `bi-*`, normalizada por `IconeBootstrap.TentarNormalizar` como nos setores e bibliotecas. Texto não reconhecível é recusado |
| Abrir em nova aba | Vem **marcado em todo cadastro novo**, local ou externo (mesma regra dos links do setor); a pessoa desmarca. Na edição mostra o valor gravado |
| Visibilidade | Todo usuário autenticado do tenant; no modo aberto de DEV, como os outros itens, aparece |
| Item ativo | Link local fica ativo pela mesma regra de prefixo dos outros itens; link externo nunca fica ativo |
| Ordem padrão | Depois dos extras, por rótulo (sem diferenciar caixa). Link novo entra no fim, como diz a tela de ordenação |
| Excluir | Apaga de vez, com confirmação; apaga junto a posição salva do link. Some do menu e da ordenação |
| Quantidade | Sem limite |

## Abordagens

Escolha da nova aba (decidida na conversa): a CSP não permite script inline, então o formulário não reage à URL digitada.

1. **Caixa marcada em todo cadastro novo (escolhida por Rafael).** Sem script; igual aos links do setor. Custo: link local também começa marcado.
2. Arquivo `.js` externo que marca/desmarca conforme a URL até a pessoa mexer. Exato ao pedido, mas é o primeiro script de formulário do `Web`.
3. Servidor decide ao salvar quando a caixa não foi tocada. Campo escondido e comportamento menos visível.

Onde guardar o link (minha escolha, para conferir):

1. **Entidade e tabela próprias, `LinkDoMenuPrincipal` (escolhida).** Quatro campos, sem árvore. O menu ganha uma terceira fonte no grupo principal, ao lado de bibliotecas e extras.
2. Reaproveitar `ItemMenu` com `SetorId` nulo. Evita tabela, mas `ItemMenu` tem FK obrigatória para setor, slug único entre irmãos, tipos, nível de acesso e a reconciliação dos setores; todo handler da árvore passaria a ter que ignorar esses itens.
3. Guardar a lista como configuração do tenant (JSON). Sem migration, mas sem id estável por link para a chave de posição e a trilha, e com edição concorrente de uma lista inteira.

## Como é construído

| Parte | O que entra |
|---|---|
| Domínio | `LinkDoMenuPrincipal` em `Domain/Menu`: `Id`, `Rotulo`, `Url`, `Icone?`, `NovaAba`. Construtor e método de edição só guardam valores já validados pela Application (mesmo padrão das outras entidades). Editar ou excluir id que não existe responde 404 |
| Banco | Tabela nova, nomes pela convenção (ADR-0017), migration em par SQL Server e PostgreSQL. Limites: rótulo 256, URL 512, ícone 64 |
| Application | Em `Application/Menu`: listar, criar, editar e excluir link. Validação da URL por `CriarItemMenuHandler.RotaEhValida` (já aceita exatamente o que esta spec pede); rótulo e limites como `CamposDeLink`. Excluir também remove a `PosicaoMenu` do grupo Principal com chave `link-{id}` |
| Chave de posição | `link-{id}`, com o `Guid` em formato `D` minúsculo (41 caracteres; cabe nos 64 da `PosicaoMenu` e no padrão `[a-z0-9-]`) |
| Catálogo | `CatalogoDeItensDoMenuPrincipal` reserva o prefixo `link-`, como já faz com `biblioteca-`: item de RCL com chave começando por ele fica fora do menu, com aviso no log |
| Navegação | `EntradaDoMenuPrincipal` (interna do `Web`) ganha `NovaAba`. `PrincipalNaOrdemPadrao` recebe os links e os põe depois dos extras. `Build` passa `NovaAba` para o `NavigationItemModel`, que já tem o campo e que os temas já tratam com `target="_blank"` e `rel="noopener noreferrer"` |
| Menu | `NavigationViewComponent` carrega os links do tenant pela Application, com o mesmo cuidado das bibliotecas: sem tenant resolvido, ou com falha, o menu segue sem eles e com aviso no log |
| Web | Telas de criar e editar e o POST de excluir, todos `[SomenteIntranetAdmin]` e com antiforgery. Sugestão: em `MenuController`, rotas `/menu/links/novo`, `/menu/links/{id}/editar`, `/menu/links/{id}/excluir`. Erros de validação voltam ao formulário com a mensagem no campo. `/menu` ganha o card Links e passa a incluir os links na lista Principal |
| Auditoria | Verbos novos `menu.link-criar`, `menu.link-editar`, `menu.link-excluir`, recurso `menu`, id do link; o JSON guarda rótulo, URL, ícone e nova aba (na edição, antes e depois; na exclusão, o que foi apagado) |

## Decisões minhas, para conferir

- **Tabela própria** em vez de `ItemMenu` com setor nulo (ver Abordagens).
- **Ordem padrão depois dos extras, por rótulo.** Combina com "item novo entra no fim da lista" da tela de ordenação.
- **Verbos de auditoria próprios** (`menu.link-*`) em vez de reaproveitar `menu.item-*`, que hoje sempre se referem a um item da árvore de um setor.
- **CRUD no `MenuController`**, que já é exclusivo do `intranet-admin` e é a tela onde o recurso mora.
- **Excluir apaga de vez**, sem arquivar, como os links do setor. A trilha guarda o que foi apagado.
- **Reservar `link-` no catálogo** é uma pequena quebra para RCLs (ver abaixo).

## ADR

ADR-0016, curta: links do menu principal são dado do tenant, cadastrados pelo `intranet-admin`, com chave `link-{id}`, URL local ou `http`/`https` e nova aba pelo `NovaAba` existente; `IItemDeMenuPrincipal` continua sendo o caminho para item em código (com rota própria e gate de visibilidade). Alternativas descartadas: as duas de armazenamento acima.

## Quebras a registrar no CHANGELOG

- Migration nova: rodar o `migrate` antes de subir a versão.
- RCL com item de menu principal cuja chave comece com `link-` deixa de aparecer (aviso no log); trocar a chave.

## Documentação

- [extensao.md](../extensao.md): acrescentar `link-` aos prefixos reservados.
- [roadmap.md](../roadmap.md): item novo.
- Plano em `docs/plans/2026-11-18-links-do-menu-principal.md`.

## Testes

| Tipo | O que cobre |
|---|---|
| Unidade | URL aceita (`/x`, `http://`, `https://`) e recusada (`javascript:alert(1)`, `data:`, `//outro.host`, `/\host`, `ftp://`, vazio, longa demais); rótulo obrigatório; ícone inválido; ordem padrão (links depois dos extras, por rótulo); posição salva leva o link para entre os fixos; prefixo `link-` recusado no catálogo; `NovaAba` chega ao `NavigationItemModel`; link externo nunca ativo |
| Integração | Só `intranet-admin` cria, edita e exclui (outro usuário recebe a resposta de acesso negado de sempre); antiforgery; mensagem no formulário para URL recusada; link aparece no menu de um usuário comum com `target="_blank"` e `rel="noopener noreferrer"` nos dois temas; excluir tira do menu, da ordenação e apaga a posição; trilha nas três ações; isolamento entre tenants; migrations nos dois bancos |
| Tela (opt-in) | Criar "Abrir um chamado" → `https://suporte.exemplo.com.br`, ver no menu, reordenar entre Mural e uma biblioteca, editar, excluir; CSP sem violação; dois temas |

## Critérios de aceite

1. O admin cria "Abrir um chamado" → `https://suporte.exemplo.com.br` com nova aba; o item aparece no grupo principal para todos os usuários e abre em nova aba.
2. `javascript:alert(1)` ou `//outro.host` é recusado, com mensagem no formulário.
3. O link pode ser reordenado em `/menu` entre os itens fixos e os extras, e a ordem vale por tenant.
4. Editar e remover funcionam; o link removido some do menu e da tela de ordenação.
5. Os temas Vertical e Horizontal mostram o item sem nenhuma mudança nas views.
6. Build sem avisos.

## O que fica para quem implementa

- Nomes exatos de classes, da tabela e das colunas, dentro das convenções do repositório.
- Texto das mensagens e do estado vazio.
- Se o card Links fica acima ou abaixo dos cards de ordenação em `/menu`.
