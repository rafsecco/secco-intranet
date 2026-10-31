# Documentos com hierarquia e ordem manual — desenho do recurso

**Data:** 2026-10-31
**Estado:** aprovado
**Origem:** issue #4 ("Bibliotecas de documentos"), parte 1 de 3. A parte 2 é substituir arquivo com
histórico de versões; a parte 3 são as bibliotecas (dono sem setor). Esta parte vale só para o
Documentos do setor, mas a lista e a tela de organizar ficam prontas para a biblioteca reaproveitar.

## Problema

O Documentos do setor é uma lista plana, ordenada pela data de publicação. O editor não escolhe a
ordem, não agrupa documentos sob um título ("Segurança da Informação" → 10 normas) e não pendura um
anexo debaixo do documento a que ele pertence ("Política de Treinamento" → "Termo de Compromisso").

Além disso, a lista está escrita direto na view do core. Um tema não consegue redesenhá-la sem
substituir a página inteira.

## Objetivos

1. Quem escreve no setor define a ordem dos documentos e uma hierarquia de dois níveis.
2. Existe o **grupo**: item sem arquivo, só título, que serve de cabeçalho dos filhos.
3. A lista é um parcial do contrato de tema (`_ListaDeDocumentos`), implementado nos temas
   `Vertical` e `Horizontal`.

## Fora de escopo

- Substituir arquivo e histórico de versões (parte 2).
- Bibliotecas, dono sem setor, permissões novas (parte 3).
- Arrastar e soltar. A ordem muda por botões, como na `/menu`.
- Mais de dois níveis.
- Mover um documento de um setor para outro.

## Anti-objetivos

- **Nada muda de lugar sem o editor ver.** Arquivar não pode deixar anexo solto na raiz, fora de
  contexto. Por isso arquivar item com filhos ativos é recusado.
- **O contrato de tema não ganha botões.** O parcial é só de leitura. Um tema que redesenha a lista
  não precisa saber de subir, descer ou arquivar.
- **A regra de quem vê não muda.** Hierarquia e ordem não mexem em permissão nem em visibilidade.

## Desenho

### 1. Modelo

O `Documento` ganha três campos:

| Propriedade | Coluna | Regra |
|---|---|---|
| `Tipo` (`TipoDeDocumento`: `Arquivo = 1`, `Grupo = 2`) | `ie_tipo` | |
| `PaiId` (Guid?) | `id_fk_documento` (FK para a própria tabela) | nulo = raiz |
| `Ordem` (int) | `nr_ordem` | posição entre os irmãos |

Nomes de coluna pela convention (ADR-0017). Índice `idx_documentos_id_fk_setor_id_fk_documento_nr_ordem`
(ou o equivalente que a convention gerar com prefixo `idx_`), porque a lista sempre lê por setor e
pai na ordem.

**Grupo:** os campos de arquivo (`NomeArquivo`, `ContentType`, `CaminhoRelativo`,
`ChaveEmbrulhada`, `Tamanho`) ficam nulos, e por isso as colunas passam a aceitar nulo. A entidade
garante os dois lados: arquivo exige todos esses campos; grupo exige que estejam vazios. Criação por
dois caminhos explícitos, um para arquivo e outro para grupo, em vez de um construtor com campos
opcionais. *(decisão de desenho: um tipo explícito deixa a regra legível na entidade; deduzir
"grupo" de "não tem arquivo" esconderia a intenção)*

O grupo não tem visibilidade própria. A coluna `ie_visibilidade` continua obrigatória e o grupo grava
`Setor`; ninguém lê esse valor para grupo. Pedir o download de um grupo devolve o mesmo resultado de
documento não encontrado.

**Hierarquia, conferida no caso de uso** (depende dos irmãos, que a entidade sozinha não enxerga):

- O pai precisa ser um item **ativo** da **raiz** do **mesmo setor**.
- Grupo é sempre raiz: nunca filho.
- Não existe neto: um item com filhos ativos não vira filho de ninguém.

A entidade expõe só a mudança de posição (`PaiId` + `Ordem`). Quem decide se a posição é válida é o
caso de uso.

### 2. Ordem

- A lista lê por `Ordem` crescente, e os filhos vêm logo abaixo do pai, também por `Ordem`.
- Toda gravação **renumera os irmãos** afetados de `0` a `n-1`, como a `/menu` faz com as posições.
  Trocar só dois valores de `Ordem` não basta, porque buracos e empates acumulam.
- **Item novo na raiz:** entra no topo (`Ordem` 0, e os demais descem uma posição). Fica parecido
  com a lista de hoje, que mostra o mais recente primeiro.
- **Item novo como filho:** entra no fim dos filhos daquele pai. Anexo se lê em sequência.
- Item arquivado sai da contagem: os irmãos ativos são renumerados.
- Não há migração de ordem dos documentos existentes, porque o banco começa vazio.

### 3. Movimentos

Cada um é um POST com antiforgery, exige escrita no setor e entra na auditoria.

| Ação | Efeito | Recusado quando |
|---|---|---|
| Subir | troca de lugar com o irmão de cima | já é o primeiro entre os irmãos |
| Descer | troca de lugar com o irmão de baixo | já é o último entre os irmãos |
| Recuar | vira o **último filho** do irmão imediatamente acima | é grupo; tem filhos ativos; é o primeiro da raiz; já é filho |
| Desrecuar | volta para a raiz, **logo depois do pai** | já está na raiz |
| Arquivar | sai da lista (como hoje) | tem filhos ativos |

Subir e descer nunca tiram um filho de dentro do pai. Para sair, usa-se desrecuar.

A mensagem da recusa de arquivar é: **"Este item tem documentos abaixo dele. Mova ou arquive esses
documentos primeiro."** As outras recusas não aparecem na prática, porque o botão sai desabilitado
na tela. Se chegarem por POST forjado, devolvem uma mensagem de erro genérica no padrão da tela.

Um item que não existe mais, ou que é de outro setor, devolve o mesmo resultado de hoje para
documento não encontrado.

### 4. Publicar

O formulário de publicar ganha o campo **"Abaixo de"**: um seletor com "Raiz" (padrão) e os itens
ativos da raiz do setor, documentos e grupos, na ordem da lista. Escolher um item publica o
documento como filho dele, no fim dos filhos.

O caso de uso de publicar confere a regra da seção 1. Se o pai escolhido deixou de valer entre abrir
o formulário e enviar (arquivado, virou filho), a publicação é recusada com erro de validação no
formulário, **antes** de gravar o arquivo no armazenamento.

### 5. Tela de organizar

- Quem escreve no setor vê, no cabeçalho da página de Documentos, a ação **"Organizar"**.
- A tela é do core, composta só com os parciais do contrato (`_PageHeader`, `_Card`, `_EmptyState`),
  no molde da `/menu`. Funciona nos dois temas e nos dois modos de cor.
- Cada linha mostra o título (grupo destacado como grupo, filho recuado) e os botões subir, descer,
  recuar, desrecuar e arquivar. O botão que não se aplica àquela linha sai desabilitado.
- O formulário **"Novo grupo"** fica nesta tela: título (obrigatório, até 256) e descrição opcional.
  O grupo entra no topo da raiz.
- Mensagens de retorno no padrão que a página de Documentos já usa (`FeedbackViewComponent`).
- Quem não escreve no setor recebe o mesmo resultado que recebe hoje ao tentar publicar sem
  permissão.

### 6. Página de leitura e o parcial `_ListaDeDocumentos`

A página monta a árvore pronta e passa ao parcial. O contrato (projeto `Secco.Intranet.Web.Theming`,
ADR-0004) ganha:

```csharp
public sealed record ListaDeDocumentosModel(IReadOnlyList<ItemDaListaDeDocumentosModel> Itens);

public sealed record ItemDaListaDeDocumentosModel(
	string Titulo,
	string? Descricao,
	bool EhGrupo,
	string? Url,          // download; nulo no grupo
	string? Icone,        // bi-* pelo tipo do arquivo; nulo no grupo
	string? Tipo,         // etiqueta curta da extensão ("PDF", "DOCX"); nulo no grupo
	string? Detalhe,      // texto pronto: "120 KB · 12/10/2026"; nulo no grupo
	BadgeModel? Badge,    // a visibilidade, como hoje; nulo no grupo
	IReadOnlyList<ItemDaListaDeDocumentosModel> Filhos);
```

- O core calcula tudo que é regra: URL, ícone, etiqueta, tamanho formatado, data, badge. O tema só
  desenha.
- Grupo sem filhos ativos não entra na árvore de leitura. *(decisão de desenho: um título vazio na
  tela parece erro)* Na tela de organizar ele aparece, para poder receber filhos ou ser arquivado.
- Lista vazia continua mostrando o `_EmptyState` da página, fora do parcial.
- `Vertical` e `Horizontal` implementam a versão padrão: ícone pelo tipo, grupo como subtítulo,
  filhos recuados. Dark e light obrigatórios (skill `secco-intranet-temas`).

O `DocumentoDto` ganha `Tipo`, `PaiId` e `Ordem`, e os campos de arquivo passam a aceitar nulo.

### 7. Auditoria

Recurso `documento` (já existe). Verbos novos:

- `documento.grupo-criar`: payload `{ setor, titulo }`;
- `documento.mover`: payload `{ setor, documento, acao, paiDe, paiPara, de, para }`, com `acao` em
  `subir`, `descer`, `recuar`, `desrecuar`.

`documento.publicar` passa a levar `pai` (nulo na raiz) no payload.

### 8. Documentação

- `docs/temas.md`: `_ListaDeDocumentos` na tabela do contrato.
- `docs/roadmap.md`: o item entregue.
- Sem ADR nova nesta parte. A ADR que registra a substituição do desenho de Coleções vem na parte 3,
  quando a biblioteca existir.

## Critérios de aceite

- [ ] O editor cria grupo, publica na raiz ou abaixo de um item, sobe, desce, recua e desrecua; a
      página de leitura reflete a ordem.
- [ ] Item novo na raiz entra no topo; anexo entra no fim dos filhos.
- [ ] Grupo não vira filho, não existe neto, e o pai é sempre um item ativo da raiz do mesmo setor —
      inclusive por POST forjado.
- [ ] Arquivar item com filhos ativos é recusado com a mensagem da seção 3; grupo vazio arquiva.
- [ ] Grupo sem filhos ativos não aparece para quem só lê; aparece no Organizar.
- [ ] Quem não escreve não vê "Organizar" nem o campo "Abaixo de", e não consegue mover por POST.
- [ ] Publicar com pai inválido é recusado antes de gravar o arquivo.
- [ ] `_ListaDeDocumentos` existe no contrato e nos temas `Vertical` e `Horizontal`, nos dois modos
      de cor.
- [ ] Migrations em par (SqlServer e Postgres), com o mesmo nome.
- [ ] Testes de unidade cobrem as regras de movimento e de hierarquia; integração cobre a tela de
      organizar, a publicação com "Abaixo de" e a recusa de arquivar.

## Decisões que ficam com quem implementar

- Nomes dos handlers, comandos e view models, seguindo os vizinhos de `Application/Documentos`.
- A rota da tela de organizar, dentro das rotas do `SetorController` (os POSTs de Documentos já usam
  o segmento literal `documentos`).
- Um handler por movimento ou um handler com a ação no comando.
- O texto das mensagens de sucesso.
