# Bibliotecas de documentos — desenho do recurso

**Data:** 2026-11-02
**Estado:** aprovado
**Origem:** issue #4 ("Bibliotecas de documentos"), parte 3 de 3. A parte 1 deu hierarquia e ordem
manual ao Documentos do setor ([spec](2026-10-31-documentos-hierarquia-e-ordem-design.md)); a parte 2,
editar documento e substituir arquivo ([spec](2026-11-01-documentos-editar-e-substituir-arquivo-design.md)).
Esta parte reaproveita as duas e substitui o desenho de Coleções
([spec de 2026-09-07](2026-09-07-colecoes-documentos-design.md)), que nunca foi implementado.

## Problema

Todo documento da Intranet pertence a um setor. Isso deixa de fora as páginas de documentos que são
da empresa e não de um setor: "Manuais", "Códigos e Políticas Internas". Elas têm três traços:

- **Todos leem.** Escreve quem o admin delegar: a TI em "Manuais", o Compliance em "Políticas".
- **Ordem manual e hierarquia rasa**, que a parte 1 já resolveu para o setor.
- **Revisão periódica**, que a parte 2 já resolveu com a substituição do arquivo.

Hoje a única saída é criar um setor de mentira ("Manuais") só para hospedar a lista. Ele carrega o
que um setor tem e uma biblioteca não precisa: os perfis `{slug}-admin`/`{slug}-user`, a árvore de
menu e os avisos. E a escrita fica presa ao perfil do setor de mentira, não ao perfil de quem
realmente cuida dos documentos.

## Objetivos

1. Existe a **biblioteca**: uma página de documentos sem setor dono, em `/bibliotecas/{slug}`,
   cadastrada pelo `intranet-admin`.
2. Quem lê e quem escreve é decidido por permissão, nos perfis que já existem, sem criar perfil novo
   por biblioteca.
3. A biblioteca tem tudo o que o Documentos do setor tem: publicar, grupos, hierarquia de dois
   níveis, ordem manual, editar, substituir arquivo e arquivar.
4. Cada biblioteca ativa é um item do grupo principal do menu, ordenável em `/menu`.

## Fora de escopo

- **Excluir biblioteca.** Ela é desativada e reativada, como o setor.
- **Mover documento entre setor e biblioteca**, ou entre duas bibliotecas.
- **Permissão global de leitura** de todas as bibliotecas, nos moldes de `setores:read`. "Todos
  leem" é por biblioteca, dado ao perfil `tenant-member` (seção 3).
- **Avisos e vencimentos em biblioteca.** A biblioteca é só de documentos.
- **Trocar o slug** depois de criado.

## Anti-objetivos

- **Nenhum documento de biblioteca fica legível por engano.** Se uma checagem esquecer o dono, o
  resultado tem de ser "não encontrado", nunca "liberado" (seção 1, visibilidade).
- **Nada de perfil próprio por biblioteca.** A ADR-0001 cria `{slug}-admin`/`{slug}-user` para setor
  porque setor é estrutura da empresa. Biblioteca é conteúdo; a delegação usa os perfis que o admin
  já tem.
- **Nenhum código duplicado por dono.** Os casos de uso de Documentos servem os dois donos. Um
  handler "de biblioteca" ao lado de cada handler "de setor" é exatamente o que esta parte evita.
- **A biblioteca inativa, inexistente ou sem permissão de leitura é indistinguível**: as três
  respondem o mesmo 404.

## Desenho

### 1. Modelo

**`Biblioteca`**, entidade nova, tabela `tb_bibliotecas` (nomes pela convention, ADR-0017):

| Propriedade | Coluna | Regra |
|---|---|---|
| `Id` (Guid) | `id_pk_biblioteca` | |
| `Nome` (string) | `ds_nome` | obrigatório, mesmo limite do nome do setor |
| `Slug` (string) | `ds_slug` | obrigatório, único, imutável; `[a-z0-9-]`, de 1 a 53 caracteres |
| `Icone` (string) | `ds_icone` | `bi-*`, validado como o do setor; vazio cai num padrão (`bi-journal-text`) |
| `Ativo` (bool) | `fl_ativo` | |
| `CreatedAt` | `dt_created_at` | |

- O limite de 53 caracteres no slug vem do menu: a chave do item é `biblioteca-{slug}`, e a chave
  aceita até 64 caracteres. *(minha decisão)*
- Índice único `uk_bibliotecas_ds_slug`.
- O slug só precisa ser único entre bibliotecas. Ele não disputa espaço com o slug de setor, porque a
  biblioteca mora sob `/bibliotecas/`. Ele só não pode coincidir com um segmento fixo das rotas
  administrativas de `/bibliotecas` (seção 5).
- A entidade espelha o `Setor`: `Renomear`, `DefinirIcone`, `Desativar` e `Ativar`. Não há `Fixo`.

**`Documento`** ganha um segundo dono possível:

| Propriedade | Coluna | Regra |
|---|---|---|
| `SetorId` (Guid → **Guid?**) | `id_fk_setor` | passa a aceitar nulo |
| `BibliotecaId` (Guid?) | `id_fk_biblioteca` | nova, FK para `tb_bibliotecas` |

- **Exatamente um dono.** A entidade garante isso na criação, por dois caminhos explícitos (setor e
  biblioteca), como a parte 1 fez com arquivo e grupo. O banco garante de novo com a check constraint
  `ck_documentos_dono` (`id_fk_setor` preenchido e `id_fk_biblioteca` nulo, ou o contrário).
  O prefixo `ck_` não está na tabela da ADR-0017; segue o padrão `<prefixo>_<tabela sem tb_>_<o quê>`
  dos outros objetos. *(minha decisão)*
- O dono não muda depois de criado.
- Índice `idx_documentos_id_fk_biblioteca_id_fk_parent_nr_ordem`, espelho do índice do setor da
  parte 1.
- **A hierarquia não cruza donos.** A regra da parte 1 ("o pai é um item ativo da raiz do mesmo
  setor") passa a ser "do mesmo dono".

**Visibilidade dentro da biblioteca.** A coluna `ie_visibilidade` continua obrigatória. Documento e
grupo de biblioteca gravam `Setor`, e ninguém lê esse valor para documento de biblioteca: quem tem
leitura da biblioteca vê tudo. A escolha é a favor do fechamento. Hoje `Empresa` libera o download
para qualquer pessoa autenticada sem olhar o dono. Gravar `Empresa` faria um caminho que esquecesse de
checar a biblioteca abrir o arquivo para todos. Gravar `Setor`, sem setor, faz o mesmo esquecimento
cair em "não encontrado". *(minha decisão)*

Migrations em par, SqlServer e Postgres, com o mesmo nome. O banco começa vazio, então não há
documento existente a converter.

### 2. Casos de uso de Documentos servem os dois donos

Os casos de uso de `Application/Documentos` (publicar, criar grupo, mover, arquivar, editar, obter
para edição, listar, baixar) hoje recebem o slug do setor, resolvem o setor e operam sobre ele. O
repositório também é por setor (`DocumentoComSetor`, `ListarPorSetorAsync`,
`ListarIrmaosAsync(setorId, …)`).

Eles passam a receber **o dono** (setor ou biblioteca) em vez do setor. Resolver o dono (existe?
está ativo?) e responder "não encontrado" quando não resolve acontece num único lugar, usado por
todos os casos de uso. O repositório passa a filtrar pelo dono. As regras de hierarquia, ordem,
arquivar com filhos, tipos e tamanho de arquivo e troca de arquivo não mudam.

O que muda por dono:

| | Setor | Biblioteca |
|---|---|---|
| Dono resolvido por | slug do setor ativo e item Documentos ligado na árvore | slug da biblioteca ativa |
| Campo "Visibilidade" no formulário | aparece | não aparece; grava `Setor` |
| Badge de visibilidade na lista | aparece | não aparece |

### 3. Permissões

O catálogo do produto (`IntranetPermissoes`, publicado pelo `migrate`, ADR-0039 da plataforma) ganha
duas famílias, no molde de `setor-{id}:*` (ADR-0043 da plataforma):

| Família | Descrição no catálogo |
|---|---|
| `biblioteca-{id}:read` | "Ler uma biblioteca de documentos específica" |
| `biblioteca-{id}:write` | "Alterar uma biblioteca de documentos específica: publicar, organizar, editar e arquivar" |

- O `{id}` é o **slug** da biblioteca, como no setor (`setor-ti:read`): `biblioteca-manuais:read`.
  O nome fica legível na tela de Acesso, e o slug imutável garante que a permissão concedida não fica
  órfã.
- **Ler** a página, ver o item no menu e baixar um documento exigem `biblioteca-{slug}:read`.
- **Escrever** (publicar, criar grupo, organizar, editar, substituir arquivo, arquivar) exige
  `biblioteca-{slug}:write`. Escrita não implica leitura no SecureGate. Quem recebe `write` recebe
  `read` junto, pela tela (seção 4), e a Intranet trata quem tem `write` como quem também lê.
  *(minha decisão: é o que o setor faz na prática, já que o `{slug}-admin` nasce com as duas)*
- O `intranet-admin` lê e escreve em toda biblioteca, como já acontece no setor.
- No modo aberto de desenvolvimento (ADR-0020), a checagem é dispensada, como no setor.

A checagem de permissão fica num componente próprio da biblioteca, irmão de `IPermissoesDeSetor` e
de `AcessoAoRecursoDoSetor`, com o mesmo comportamento: admin passa direto e cada slug é uma chamada
ao `IAuthorizationService` com a policy dinâmica de permissão.

### 4. Tela de Acesso: conceder permissão de biblioteca a qualquer perfil

Hoje a tela de um perfil em `/acesso` só oferece permissão de setor no próprio perfil daquele setor
(`{slug}-admin`/`{slug}-user`). Para biblioteca isso não serve, porque a delegação é justamente dar a
biblioteca a um perfil que já existe: "Todos os membros" lê, `ti-admin` escreve em Manuais.

A tela de **todo perfil**, inclusive o implícito "Todos os membros" (`tenant-member`, ADR-0056 da
plataforma), ganha a seção **"Bibliotecas"**:

- Uma linha por biblioteca **ativa**, em ordem de nome, com duas caixas: "Ler" e "Alterar".
- **"Alterar" inclui "Ler".** Ao salvar, o caso de uso acrescenta `biblioteca-{slug}:read` a todo
  `biblioteca-{slug}:write` da lista. O rótulo diz isso: "Alterar (inclui ler)". A regra fica no
  servidor, e não em script, porque a tela de Acesso não carrega script próprio e a CSP não aceita
  script inline. *(minha decisão)*
- Salvar usa o mesmo envio de hoje (`EditarPermissoes`, que define a lista inteira do perfil).
- Permissão de biblioteca inativa que o perfil já tem continua aparecendo como "extra", pela regra
  que a tela já tem para permissão fora do catálogo. Assim, salvar o formulário não a apaga sem o
  admin ver.

### 5. Cadastro de bibliotecas (`intranet-admin`)

- **Lista** em `/bibliotecas`, exclusiva do `intranet-admin` (`[SomenteIntranetAdmin]`), com nome,
  slug, ícone e situação, e as ações editar, desativar e reativar. Item **"Bibliotecas"** no grupo
  Administração do menu, ao lado de "Setores".
- **Criar:** nome, slug, ícone e a caixa **"Todos os membros leem"**, que vem **marcada**. *(minha
  decisão: os casos que motivaram a issue, Manuais e Políticas, são da empresa toda)*
  - Marcada, o caso de uso garante `biblioteca-{slug}:read` no perfil `tenant-member`
    (`IGestaoDeAcesso.GarantirPermissoesAsync`, que mescla e nunca remove) **antes** de gravar a
    biblioteca. É a mesma ordem da ADR-0001 para as Roles do setor: uma permissão órfã no SecureGate
    é inofensiva, e repetir a operação não duplica nada. O contrário, uma biblioteca gravada que
    todos deveriam ler e ninguém lê, seria pior. Se o SecureGate falhar, a criação falha com a
    mensagem de erro de sempre e nada é gravado.
  - Desmarcada, a biblioteca nasce visível só para o `intranet-admin`, até alguém conceder leitura
    em `/acesso`.
  - Quem escreve não é escolhido aqui. Depois de criar, a tela leva à lista com uma mensagem
    lembrando de conceder "Alterar" em `/acesso` ao perfil responsável.
- **Editar:** nome e ícone. O slug aparece só para leitura.
- **Desativar/reativar:** a biblioteca inativa some do menu e responde 404 na página e no download
  dos documentos dela. Documentos, permissões concedidas e posição no menu ficam guardados, e voltam
  como estavam ao reativar.
- **Slug:** as mesmas regras de formato do setor e o limite de 53 caracteres. É recusado se já
  existir outra biblioteca com ele, ou se coincidir com um segmento fixo das rotas administrativas de
  `/bibliotecas` (por exemplo, o de "nova"). A lista desses segmentos sai das rotas registradas, como
  o `ISlugsReservados` faz para o setor, e não de uma lista escrita à mão.
- `bibliotecas` passa a ser um slug reservado para **setor** automaticamente, porque o
  `ISlugsReservados` deriva das rotas registradas. Basta conferir isso num teste.

### 6. Página da biblioteca

- `/bibliotecas/{slug}`: cabeçalho com nome e ícone, e a lista pelo parcial `_ListaDeDocumentos` da
  parte 1, sem mudança no contrato de tema. O badge de visibilidade não aparece (seção 2).
- Quem escreve vê as mesmas ações do Documentos do setor: publicar (com o campo "Abaixo de"),
  "Organizar" (com "Novo grupo"), editar e arquivar.
- As rotas de escrita ficam sob `/bibliotecas/{slug}/documentos/...` e espelham as do setor em
  `/{slug}/documentos/...`.
- O download continua em `/documentos/{id}/download`, o único caminho até o conteúdo (ADR-0005). O
  caso de uso de baixar passa a olhar o dono: documento de biblioteca exige leitura daquela
  biblioteca, e a visibilidade não é consultada.
- As telas de Documentos do core (lista, organizar, editar) são as mesmas para os dois donos. O que
  difere (título, trilho, rotas, campo de visibilidade) vem do view model. Duplicar as views por dono
  fica fora (ver anti-objetivos).

### 7. Menu

- Cada biblioteca ativa em que a pessoa tem leitura entra no **grupo principal**, com chave
  `biblioteca-{slug}`, rótulo = nome, ícone da biblioteca e destino `/bibliotecas/{slug}`.
- **Ordem padrão:** depois de Mural e Diretório, e antes dos itens extras de projetos externos, por
  nome. *(minha decisão: as bibliotecas são do core e não devem ficar abaixo de extensões)* A
  posição escolhida pelo admin em `/menu` vale como para qualquer item (`tb_posicoes_menu`, pela
  chave).
- `/menu` lista todas as bibliotecas ativas para ordenar, independentemente de permissão, como já faz
  com os extras.
- **O prefixo `biblioteca-` fica reservado** no `CatalogoDeItensDoMenuPrincipal`: um item de extensão
  com chave que começa por ele fica fora do menu, com aviso no log, como as chaves `mural` e
  `diretorio`. Sem isso, um item externo poderia tomar a posição de uma biblioteca.
- A montagem continua em `IntranetNavigation.PrincipalNaOrdemPadrao`, que passa a receber as
  bibliotecas visíveis. Quem filtra por permissão é o `NavigationViewComponent`, como faz com os
  setores.

### 8. Auditoria

- Recurso novo `biblioteca`, com os verbos `biblioteca.criar`, `biblioteca.editar`,
  `biblioteca.desativar` e `biblioteca.reativar`, no molde dos de setor. O de criar leva
  `todosLeem` no payload.
- Os verbos de documento não mudam. No payload, onde hoje vai `setor`, documento de biblioteca leva
  `biblioteca` (o slug). Nunca os dois.

### 9. Documentação

- **ADR nova** em `docs/adr/secco-intranet-adrs.md`: "Bibliotecas: documentos sem setor dono".
  Registra o dono XOR, as permissões por biblioteca nos perfis existentes (e por que não há perfil
  por biblioteca, contrastando com a ADR-0001), o uso do `tenant-member` para "todos leem" e que este
  desenho substitui o de Coleções.
- A spec de Coleções passa a ter **Estado: substituída** pela ADR nova e por esta spec.
- `docs/extensao.md`: o prefixo `biblioteca-` reservado entre as chaves de item de menu.
- `docs/guia-de-deploy.md`: as famílias novas entram no catálogo pelo `migrate`, como as de setor.
- `docs/roadmap.md`: o item da issue #4 vira `[x]`, sem "falta a parte 3".
- Skill `secco-intranet-setores`: a seção "Recursos sem setor dono" ganha a biblioteca como o caso
  de dono alternativo **com permissão por instância** (diferente do Inventário, que é um perfil fixo).

## Critérios de aceite

- [ ] O `intranet-admin` cria, edita, desativa e reativa bibliotecas em `/bibliotecas`. Mais ninguém
      chega lá.
- [ ] Criar com "Todos os membros leem" marcada dá `biblioteca-{slug}:read` ao `tenant-member`
      antes de gravar. Falha no SecureGate não grava a biblioteca.
- [ ] A biblioteca ativa aparece no grupo principal do menu só para quem lê, e pode ser ordenada em
      `/menu`.
- [ ] Biblioteca inexistente, inativa ou sem leitura dá o mesmo 404 na página, e documento dela dá o
      mesmo 404 no download.
- [ ] Com `biblioteca-{slug}:write`, a pessoa publica, cria grupo, organiza, edita, substitui arquivo e
      arquiva. Sem ela, não vê essas ações e não consegue fazê-las por POST forjado.
- [ ] Todo documento tem exatamente um dono, garantido na entidade e no banco
      (`ck_documentos_dono`). A hierarquia não cruza donos, nem por POST forjado.
- [ ] O formulário de documento da biblioteca não tem o campo de visibilidade, e a lista não mostra o
      badge.
- [ ] A tela de todo perfil em `/acesso`, inclusive "Todos os membros", oferece "Ler" e "Alterar" de
      cada biblioteca ativa.
- [ ] O Documentos do setor continua igual: os testes de integração atuais passam sem mudança de
      comportamento.
- [ ] `bibliotecas` é slug reservado para setor, e o prefixo `biblioteca-` é chave reservada no menu.
- [ ] As famílias `biblioteca-{id}:read` e `biblioteca-{id}:write` estão no catálogo publicado.
- [ ] Migrations em par (SqlServer e Postgres), com o mesmo nome.
- [ ] Testes de unidade cobrem a invariante de dono, a hierarquia por dono e a resolução de
      permissão. A integração cobre o cadastro, a página, a escrita e o 404 uniforme.
- [ ] A ADR nova e os documentos da seção 9 estão atualizados.

## Decisões que ficam com quem implementar

- Como representar o dono nos casos de uso e no repositório (um tipo com duas variantes, um par de
  ids, o que for mais legível ao lado dos vizinhos), desde que a resolução fique num lugar só.
- Nomes de handlers, comandos, controller e view models, seguindo os vizinhos.
- As rotas administrativas exatas sob `/bibliotecas`, respeitando a seção 5 (o segmento fixo não pode
  ser slug de biblioteca).
- Se o fumaça contra o SecureGate real (`roteiro-fumaca-securegate.md`) ganha um passo para a
  permissão de biblioteca no `tenant-member`.
- Se o seed de desenvolvimento (`SetoresDesenvolvimentoSeeder` ou um vizinho) cria uma biblioteca de
  exemplo.
- O texto das mensagens de sucesso.
