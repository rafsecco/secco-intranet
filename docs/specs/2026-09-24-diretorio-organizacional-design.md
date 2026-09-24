# Diretório organizacional — desenho do recurso

**Data:** 2026-09-24
**Estado:** rascunho, aguardando revisão

## Problema

Hoje o diretório é demonstração: `/diretorio` e `/diretorio/perfil` mostram oito pessoas
fictícias, atrás de `Intranet:Demo:Habilitado`. O SecureGate só conhece `Id`, `Email`, `Status`
e roles de cada usuário — não há nome, cargo, ramal, foto, setor de lotação nem gestor. Sem
isso, a Intranet só consegue mostrar uma pessoa pelo e-mail: é assim no Inventário ("atribuído
a") e no relatório de notificação do Mural.

O objetivo é o diretório virar recurso real: buscar pessoas, ver o perfil de cada colaborador
e enxergar o organograma, com o RH (ou quem o `intranet-admin` delegar) mantendo os dados.

## O que o levantamento apurou

| Pergunta | Resposta |
|---|---|
| O SecureGate tem nome de exibição? | **Não**, em nenhum DTO nem na conta do usuário que a plataforma acabou de entregar. Pedido em [secco-platform#30](https://github.com/rafsecco/secco-platform/issues/30) |
| Dá para listar as pessoas do tenant? | Sim — `ListUsers`, com `Status`; **não pagina**, devolve o tenant inteiro |
| `UsuarioDoTenant` (o que a Intranet já lê) serve? | Só em parte: tem `Id`, `Email` e `Roles`, **sem a situação da conta**; o diretório precisa filtrar desativados |
| O armazenamento de arquivos dos Documentos serve para foto? | Serve a **cifragem e o isolamento por tenant** (`IArquivoStore`). Não serve como Documento: este exige setor, título e visibilidade e aparece nas listas do setor. E o caminho gravado é opaco, sem pasta |
| Existe SDK de imagem na plataforma? | **Não.** Pedida em [secco-platform#29](https://github.com/rafsecco/secco-platform/issues/29) |
| Quem usa o flag de demonstração? | Só o diretório (menu, "Meu perfil" e o controller). Sai junto com a demonstração |

## Decisões

| Eixo | Decisão |
|---|---|
| Fronteira de dados | Identidade (id, e-mail, situação) **continua no SecureGate**; localmente fica só um perfil complementar por usuário, sem espelho de usuários |
| Como o diretório se monta | Junção em memória de `ListUsers` com o perfil local, com uma cache curta por tenant (~60 s). Um espelho local foi recusado: duplica identidade e exige sincronização |
| Nome de exibição | **Local agora, atrás de uma porta**; quando a plataforma entregar o `displayName` (#30), o adaptador troca a origem e uma cópia única migra os dados. Duplicação temporária e deliberada |
| Quem acessa | **Só três perfis**: `intranet-admin` e `diretorio-admin` (acesso total) e `diretorio-user` (ver e editar o próprio contato). Quem não tem nenhum dos três não vê o item de menu e recebe 403 em toda rota — inclusive `{slug}-admin`, `{slug}-user` e `inventario-admin`. O Diretório é um item de menu fixo, como o Inventário, e não define perfis "padrão" para setores |
| Quem edita o quê | **Dados divididos** (ver "Autorização"): cargo, setor e gestor só o admin do diretório; nome, ramal e "sobre" o próprio colaborador e o admin |
| Perfis obrigatórios | `diretorio-admin` e `diretorio-user`: Roles fixas do produto, atribuíveis pelo `intranet-admin` na tela de acesso; o `intranet-admin` sempre tem o mesmo acesso do `diretorio-admin` |
| Setor de uma pessoa | **Lotação explícita** (campo escolhido pelo admin entre os setores), independente das Roles. Acesso não é lotação |
| Organograma | Árvore por gestor ("reporta a"), definido pelo admin, com regra contra ciclo |
| Carga inicial | Edição pessoa a pessoa **e** importação CSV pelo admin |
| Foto | Entra neste corte, mas como **última etapa**, ligada à SDK de imagem da plataforma (#29) por uma porta. Sem SDK, avatar por iniciais |
| Demonstração | Sai. No lugar, um seeder e um adaptador de DEV para o modo sem SecureGate |

## Dados

`PerfilColaborador`, uma linha por usuário do SecureGate que tenha algo a dizer, no banco do tenant
(ADR-0005). O perfil **nasce na primeira edição**; quem não tem perfil aparece pelo e-mail.

| Campo | Regra |
|---|---|
| `UsuarioId` | Guid do usuário no SecureGate. **Único**, sem FK (identidade é da plataforma) |
| Nome de exibição | Até 120 caracteres. Sem nome, a tela usa o e-mail |
| Cargo | Até 120. Só o admin do diretório edita |
| Ramal | Até 20 |
| Sobre | Até 500 |
| Setor (lotação) | FK opcional para o setor. Só aceita setor **existente e ativo**. Só o admin edita |
| Gestor | Guid do usuário-gestor, sem FK. Só o admin edita |

**Regras de gestor:**

- ninguém é gestor de si mesmo;
- não há ciclo (A reporta a B, B reporta a A, ou uma volta mais longa) — checado antes de gravar;
- o gestor precisa ser um usuário ativo do tenant;
- se o gestor depois for desativado, a equipe **não some**: vira raiz do organograma com a marca
  "gestor inativo".

Setor desativado depois da lotação: a lotação é mantida e o badge aparece esmaecido; só novas
lotações exigem setor ativo.

As colunas da foto **entram na última etapa**, na migration dela, para não existir coluna sem uso.

## Autorização

| Quem | Vê o diretório | Edita o próprio (nome, ramal, sobre, foto) | Edita cargo, setor, gestor | Edita os outros | Importa CSV |
|---|---|---|---|---|---|
| `intranet-admin` | sim | sim | sim | sim | sim |
| `diretorio-admin` | sim | sim | sim | sim | sim |
| `diretorio-user` | sim | sim (só o próprio) | **não** | não | não |
| Nenhum dos três (inclui `{slug}-admin`, `{slug}-user`, `inventario-admin`) | **não** (403; sem item de menu) | não | não | não | não |

Dois níveis, então: **usuário** (`diretorio-user`) e **administrador** (`diretorio-admin` ou
`intranet-admin`). Ter o perfil de um setor, ou o do Inventário, **não** abre o Diretório — são
autorizações independentes, como a ADR-0008 já estabelece para a área administrativa.

- **Quem aparece é diferente de quem acessa.** O diretório **lista todo usuário ativo do tenant**,
  tenha ou não um dos três perfis; os perfis só decidem quem pode **olhar e editar**.
- **Identificação do "próprio":** o claim `sub` do usuário logado é o `UsuarioId`. Nunca se aceita
  um id vindo do formulário para saber "quem sou eu".
- **Comandos separados.** Editar o próprio contato e editar dados funcionais são **dois comandos**,
  em dois handlers. O formulário do colaborador nem tem os campos funcionais, e o servidor os
  ignora mesmo que alguém os forje — fechar isso só na tela seria a falha clássica de
  *mass assignment*.
- **`diretorio-admin` e `diretorio-user` são Roles fixas**, no mesmo molde do `inventario-admin`.
  Uma função única calcula o nível do usuário (`Nenhum`, `Usuario` ou `Administrador`) a partir das
  Roles — `intranet-admin` e `diretorio-admin` dão `Administrador`, `diretorio-user` dá `Usuario`,
  o resto dá `Nenhum` — e **todo** controle de acesso do Diretório passa por ela: o gate das rotas, o
  item de menu e o link "Meu perfil" do menu do usuário. Trocar "nome da Role" por "permissão"
  depois é uma linha.
- **As duas entram em `ClassificacaoDePerfil.PerfisDoProduto`**, então a tela de acesso passa a
  oferecer criá-las na seção "Perfis do produto".
- **Consequência operacional a conhecer:** até o modelo de permissões existir, o `diretorio-user`
  precisa ser atribuído **usuário a usuário** na tela de acesso. Ninguém vê o Diretório por padrão.
  O perfil agrupador que resolve isso — um perfil como `todos` que carrega a permissão de usuário do
  Diretório, dado uma vez a todo mundo — é exatamente o que a spec seguinte da área de acesso
  entrega. A importação CSV **não** atribui perfis.
- **Delegar para um perfil inteiro** (o RH todo em vez de usuário a usuário) vem da mesma spec.
- **Desativados não aparecem** no diretório.

### Caminho para o modelo de permissões

Para a troca ser mecânica quando a spec de permissões chegar, os nomes já ficam definidos no formato
da plataforma (`recurso:acao`):

| Nível hoje (Role fixa) | Permissão equivalente depois |
|---|---|
| `Usuario` (`diretorio-user`) | `diretorio:read` — ver e editar o **próprio** contato |
| `Administrador` (`diretorio-admin`) | `diretorio:manage` — tudo, inclusive dados funcionais e importação |
| `intranet-admin` | implícito: sempre `Administrador`, sem depender de permissão |

Um perfil agrupador (`todos`) poderá então carregar `diretorio:read` junto das leituras de setor,
dado **uma vez** a cada pessoa — que é o que remove a limitação operacional acima e mantém pequeno o
número de Roles na sessão. A única função de nível de acesso passa a consultar permissões em vez de
nomes de Role; gate, menu e "Meu perfil" não mudam.

## Telas

Views no core, só com os partials do contrato de tema. **Todas** as rotas exigem nível `Usuario` ou
`Administrador`; as marcadas "admin" exigem `Administrador`.

- **`/diretorio`** — grade de pessoas com busca (nome, cargo, setor, e-mail) e filtro por setor;
  24 por página, paginação em memória.
- **`/diretorio/{id}`** — perfil da pessoa: contato, setor, "reporta a" e "equipe direta". Para o
  admin e para o dono do perfil, com o botão de editar.
- **`/diretorio/perfil`** — "Meu perfil": os campos que o colaborador pode editar. É o link que o
  menu do usuário já tem.
- **`/diretorio/{id}/editar`** *(admin)* — o admin edita todos os campos de qualquer pessoa.
- **`/diretorio/organograma`** — ver abaixo.
- **`/diretorio/importar`** *(admin)* — ver abaixo.

O item "Diretório" do menu e o link "Meu perfil" do menu do usuário só aparecem para quem tem nível
`Usuario` ou `Administrador`.

Sem SecureGate configurado (DEV e Testing) o diretório explica isso e não quebra — a mesma regra
da área de acesso.

### Modo DEV sem SecureGate

A demonstração estática e o flag `Intranet:Demo:Habilitado` saem, junto com `DemoOptions`,
`DiretorioDemonstracao` e `DemonstracaoTests`. No lugar:

- um **seeder de desenvolvimento** (mesmo padrão dos seeders de setor e de publicação, com a guarda
  dupla da ADR-0019) cria colaboradores fictícios;
- um **adaptador de DEV** os expõe como usuários do tenant, só em `Development` **e** sem
  SecureGate configurado.

Isso mantém o tema exercitável em DEV sem nunca servir gente inventada numa intranet em uso — o
risco que o flag existia para conter.

## Organograma

`/diretorio/organograma`: árvore por gestor, em blocos `<details>` aninhados (recolhe e expande sem
JavaScript e funciona nos dois temas).

- **Raízes:** quem tem equipe e não tem gestor ativo. Quem **não tem gestor nem equipe** vai para uma
  seção recolhida "Sem posição no organograma" — senão o topo viraria uma lista plana de centenas
  de nomes.
- **Gestor inativo:** a equipe vira raiz, com a marca "gestor inativo".
- **Teto de profundidade (20).** A regra de ciclo impede o problema; o teto evita laço infinito se
  alguém alterar o banco à mão.
- Ordenação por nome, sem diferenciar caixa.

## Importação CSV

`/diretorio/importar`, para o `diretorio-admin` e o `intranet-admin`.

- **Formato:** cabeçalho `email;nome;cargo;ramal;setor;gestor`. Aceita `;` ou `,` (o Excel em
  português salva com `;`), UTF-8 com ou sem BOM. `setor` é o **slug**; `gestor` é o **e-mail**.
  Limite de 1 MB e 5 mil linhas.
- **Não cria usuário:** o e-mail precisa existir no SecureGate e estar ativo; provisionar usuário é
  outro assunto (convite, ADR-0033 da plataforma). E-mail desconhecido é erro da linha.
- **Duas etapas.** A **pré-visualização** valida tudo **sem gravar**: por linha, o erro; no total,
  quantos serão criados, atualizados e ignorados. A **confirmação** aplica as linhas válidas. Linhas
  inválidas não bloqueiam as válidas, e o relatório final é o mesmo da pré-visualização.
- **Célula vazia = não alterar**, nunca "limpar". Reimportar o mesmo arquivo é seguro e não muda nada.
- **Gestores em duas passadas:** primeiro os perfis, depois os "reporta a", com a checagem de ciclo
  feita sobre o **lote inteiro** — uma linha pode referenciar um gestor que só existe mais abaixo
  no mesmo arquivo.
- **Segurança da entrada:** o conteúdo é lido como texto e nunca interpretado; tamanho e número de
  linhas limitados antes de processar; nome de campo desconhecido no cabeçalho é erro, não ignorado
  em silêncio.

## Foto (última etapa, depende de secco-platform#29)

O avatar por iniciais continua como padrão. Com a SDK de imagem entregue:

- **Portas:** `IProcessadorDeImagem` na Application. O adaptador nasce da SDK; até lá existe um
  adaptador "indisponível" e a tela **esconde** o botão de enviar.
- **Armazenamento:** o mesmo `IArquivoStore` (mesma cifragem e isolamento por tenant), com uma
  **pasta lógica** `diretorio/fotos` — o que pede uma extensão pequena da porta, que hoje só
  gera caminho opaco. A foto **não** é um Documento.
- **Reenvio substitui:** grava o novo arquivo, troca a referência no perfil e só então remove o
  antigo. Se a remoção falhar, fica um arquivo órfão e um aviso no log — nunca uma referência
  quebrada.
- **Entrega:** `/diretorio/{id}/foto`, **só para autenticados**, com `Cache-Control: private` e
  `ETag` por versão da foto.
- **Recorte e redução:** a tela de recorte (arrastar e dar zoom) é do produto, com uma biblioteca de
  licença MIT embutida no próprio projeto, sem CDN. O servidor recebe **só as coordenadas** e
  recorta e reduz pela SDK — não confia no canvas do navegador, porque o arquivo que chega
  precisa ser revalidado de qualquer jeito.
- **Quem envia:** o colaborador (a própria foto) e o admin do diretório (a de qualquer um).
  Remover a foto volta às iniciais.
- **Tema:** o contrato de tema ganha um partial `_Avatar` (foto ou iniciais); hoje o avatar é
  markup solto nas views. Muda o contrato nos dois temas.

## Auditoria

Verbos novos em `VerbosDeAuditoria`, recurso `diretorio`:

| Verbo | Metadata |
|---|---|
| `diretorio.perfil-editar` | id do usuário e **nomes** dos campos alterados |
| `diretorio.dados-funcionais-editar` | id do usuário e nomes dos campos alterados |
| `diretorio.importar` | totais: criados, atualizados, ignorados, com erro |
| `diretorio.foto-alterar` / `foto-remover` | id do usuário |

O que vai para a trilha são **nomes de campo**, não valores — a trilha diz o que aconteceu sem virar
cópia do cadastro. Leitura não é auditada.

## Testes

- **Unitários:** ciclo e autogestor; lotação só em setor ativo; os dois comandos separados (o
  contato não altera cargo); montagem da árvore (gestor inativo, sem posição, teto de
  profundidade); leitor de CSV (delimitadores, BOM, aspas, limites, cabeçalho inválido, e-mail
  desconhecido, gestor no mesmo lote, ciclo dentro do lote).
- **Integração:** matriz de autorização em toda rota, `GET` e `POST`, com o gate rodando antes do
  antifalsificação (403, não 400):
  - **sem nenhum dos três perfis** — sem role, `{slug}-admin` (de um ou vários setores),
    `{slug}-user` e `inventario-admin` — bloqueado em **toda** rota, e sem o item de menu nem o link
    "Meu perfil";
  - **`diretorio-user`** vê tudo e edita **só o próprio**; recebe 403 em editar outro, nos dados
    funcionais e na importação; e um `POST` com campo funcional forjado **não** o altera;
  - **`diretorio-admin`** e **`intranet-admin`** liberados em tudo.

  Persistência: `UsuarioId` único; migrations nos dois provedores.
- **Fumaça:** acrescenta-se à do SecureGate real a junção do diretório com a lista de usuários.

## Etapas de entrega

1. **Domínio e persistência** — `PerfilColaborador` e as migrations dos dois provedores.
2. **Leitura real** — junção, busca, perfil da pessoa, `UsuarioDoTenant` com a situação da conta,
   adaptador e seeder de DEV, e o fim da demonstração.
3. **Acesso e edição** — o nível de acesso (`Nenhum`/`Usuario`/`Administrador`) com o gate, o item
   de menu e o "Meu perfil" condicionais; `diretorio-admin` e `diretorio-user` na tela de acesso;
   "Meu perfil", edição pelo admin, auditoria e a matriz de autorização.

   *Nota de ordem:* o gate de acesso (nível e atributo) entra **junto da etapa 2**, não depois — as
   telas de leitura também são restritas, e a matriz negativa nasce com elas.
4. **Organograma** — a árvore e as regras de gestor e ciclo.
5. **Importação CSV.**
6. **Foto** — bloqueada pela #29; as etapas 1–5 entregam sem ela.

## Fora de escopo

- **Nome de exibição na plataforma** (#30): quando sair, é troca de adaptador e cópia única.
- **Sincronizar cargo, gestor e foto a partir do AD/Entra:** depende da plataforma ler o diretório
  ([#27](https://github.com/rafsecco/secco-platform/issues/27),
  [#28](https://github.com/rafsecco/secco-platform/issues/28)); se um dia existir, popula o que
  hoje a importação popula.
- **Inventário e notificação passando a ignorar usuários desativados** e a exibir o nome de
  exibição: o campo de situação passa a existir, mas os dois consumidores **não mudam** neste
  corte.
- **Campos extras** (celular, aniversário, habilidades): sem demanda declarada.
- **Dar acesso ao Diretório a um perfil inteiro** — `diretorio-user` para todo mundo de uma vez, ou a
  administração para o RH todo — em vez de usuário a usuário: depende do modelo de permissões, a
  spec seguinte da área de acesso.
- **Criar ou convidar usuário** pela importação.

## Documentação a atualizar junto

- `docs/roadmap.md`: o item "Diretório organizacional" passa a apontar para esta spec.
- `docs/plataforma.md`: já registra #29 e #30.
- `src/Secco.Intranet.Web/appsettings.Development.json`: a seção `Intranet:Demo` sai (o flag só
  existe ali e no roadmap; README e `.env.example` não o citam).
