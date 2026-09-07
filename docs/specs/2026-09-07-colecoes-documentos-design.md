# Coleções de Documentos — desenho do recurso

**Data:** 2026-09-07
**Estado:** aprovado em chat, aguardando revisão da spec escrita

## Problema

Documento hoje é um recurso plano: cada arquivo publicado no setor aparece solto na aba
`/setor/{slug}/documentos`, sem forma de agrupar vários arquivos sob um mesmo assunto. O caso de
uso que expôs a lacuna é concreto — um setor (tipicamente RH) publica várias políticas
internas (código de conduta, plano de saúde, política de home office) e quer que elas apareçam
juntas, sob um título comum ("Políticas da empresa"), em vez de misturadas com qualquer outro
arquivo solto que o setor tenha publicado.

Este documento fecha o desenho do agrupamento — recurso novo dentro de Documentos, ainda sem
item correspondente no roadmap porque a necessidade surgiu nesta sessão.

## Decisões

| Eixo | Decisão |
|---|---|
| Dono da coleção | Um **setor**, igual ao Documento hoje — reaproveita a Role `{slug}-admin` (ADR-0001), sem autorização nova |
| Modelo de agrupamento | Entidade **`Colecao`** própria, não um campo de texto livre no Documento |
| Visibilidade | A coleção tem visibilidade própria (`Setor`/`Empresa`); todo documento dentro **herda** a dela. Documento avulso (fora de coleção) mantém visibilidade própria, como hoje |
| Navegação | Item fixo novo **`/documentos`** no menu, listando as coleções visíveis — não um item dinâmico por coleção |
| Onde se administra | Dentro da aba existente `/setor/{slug}/documentos`, como uma seção a mais — sem tab nova |
| Documento avulso | Continua existindo exatamente como hoje, e **não** aparece na vitrine `/documentos` |

### Por que herdar visibilidade em vez de manter por documento

Um documento com visibilidade própria dentro de uma coleção "da empresa" cria o caso confuso de
um caderno público com um arquivo escondido dentro — ninguém que abre a coleção entende por que
um item sumiu. Herdar elimina esse caso por construção: mudar a visibilidade da coleção depois
já vale para tudo dentro dela, sem tocar em cada documento.

### Por que vitrine única em vez de item de menu por coleção

Um item de menu nomeado por coleção ("Políticas da empresa" aparecendo com esse nome na
navegação) foi cogitado e descartado: exigiria navegação dinâmica agora, que é exatamente o que
`ItemMenu` — tabela autorecursiva do roadmap, Fase 2 — vai construir de propósito. Resolver isso
duas vezes é o risco que se evita adiando: a vitrine única não compete com o `ItemMenu` porque
não tenta ser navegação livre, é só mais uma página fixa como o Mural.

### O que ficou de fora, e por quê

- **Mover um documento avulso para dentro de uma coleção depois de publicado.** Fora do desenho:
  o caso de uso é publicar já agrupado, não reorganizar o que já existe.
- **Reordenar coleções ou documentos dentro delas.** Ordenação é por data, igual ao resto do
  produto.
- **Paginação em `/documentos/{id}`.** Mesma dívida que `ListarPorSetorAsync` já tem hoje —
  registrada, não nova.
- **Descrição rica (Markdown) na coleção.** `Descricao` é texto plano curto, para o card da
  vitrine — o mesmo motivo que mantém o corpo de um Documento sem formatação.

## Modelo

`Colecao`, em `Domain/Documentos/`, herdando `BaseEntity`.

```text
tb_colecoes
  id_pk_colecao     Guid
  id_fk_setor       Guid, obrigatorio, Restrict
  ds_titulo         256
  ds_descricao      512, opcional
  ie_visibilidade   int    Setor=0 | Empresa=1
  fl_ativo          bool
  ds_criado_por     256
  dt_created_at     DateTimeOffset
  dt_atualizado_em  DateTimeOffset?
```

Índice: `idx_colecoes_id_fk_setor`.

### Alteração em `tb_documentos`

```text
id_fk_colecao     Guid?, Restrict          -- novo
ie_visibilidade   int?                     -- deixa de ser NOT NULL
```

Índice novo: `idx_documentos_id_fk_colecao`.

### Invariantes de domínio

**`Colecao`**: título não vazio (mesmo limite de `IntranetOptions.MaxNameLength` que Setor e
Publicação já usam).

**`Documento`** ganha uma invariante XOR: exatamente um entre `ColecaoId` e `Visibilidade` está
presente — nunca os dois, nunca nenhum. Um documento sem coleção exige visibilidade própria; um
documento com coleção não aceita visibilidade própria, porque ela não seria usada por
ninguém — a leitura sempre resolve pela coleção.

### Comportamento

- `Colecao(setorId, titulo, descricao, visibilidade, criadoPor)`
- `Colecao.Editar(titulo, descricao, visibilidade)` — revalida a invariante de título
- `Colecao.Arquivar()` — `Ativo = false`
- `Documento` ganha um parâmetro `colecaoId` opcional no construtor existente; `visibilidade`
  passa a `Visibilidade?`. A validação do XOR entra junto das validações já existentes.

## Autorização e ciclo de vida

Sem role nova. `{slug}-admin` do setor dono cria, edita e arquiva coleções do próprio setor —
mesma Role que já publica documento e aviso. Qualquer admin daquele setor pode publicar dentro
de qualquer coleção ativa do setor a qualquer momento; não há conceito de "dono do documento
dentro da coleção" além de `CriadoPor` como registro de exibição, igual a Documento hoje.

Arquivar uma coleção tira ela e o conteúdo da vitrine pública (`/documentos`), mas a aba do
setor continua mostrando tudo — mesmo padrão que Publicação já tem entre o Mural público e a
aba `/setor/{slug}/avisos`.

## Consulta e visibilidade

Duas leituras, com a mesma regra de visibilidade que Mural e Documento avulso já aplicam
(setores do leitor via Role, ou dispensada no modo aberto de DEV):

**Vitrine (`/documentos`)** — lista coleções ativas e visíveis, paginada:

```sql
fl_ativo
AND (ie_visibilidade = 1                      -- Empresa
     OR id_fk_setor IN (@setoresDoLeitor))    -- Setor
```

**Detalhe (`/documentos/{id}`)** — resolve a coleção pela mesma regra acima (coleção inexistente,
arquivada ou fora do alcance devolvem o **mesmo erro** — o mesmo motivo do Documento avulso:
distinguir revelaria a existência da coleção a quem não pode vê-la) e, uma vez autorizada, lista
os documentos ativos com aquele `ColecaoId`, sem checar visibilidade de novo por documento — ela
já foi resolvida pela coleção.

## Web

**Novo controller `ColecoesController`**, rota `/documentos`:
- `Index` — vitrine paginada, mesmo padrão do `MuralController` (handler resolvido sob demanda,
  mural vazio sem tenant resolvido).
- `Detalhe(id)` — lista os documentos da coleção; `NotFound()` no erro único acima.

**`SetorController`**, dentro da aba `Documentos` existente:
- Seção "Coleções deste setor": lista as coleções (ativas e arquivadas, como a aba de Avisos já
  faz), com formulário inline de criar/editar e botão de arquivar.
- O formulário de publicar documento ganha um seletor: "Avulso" ou uma das coleções ativas do
  setor. Selecionar uma coleção esconde o campo "Quem pode ver" no formulário (a visibilidade já
  está decidida pela coleção) — mesmo princípio de menos campo a errar que o resto do produto
  segue.

**Menu**: `IntranetNavigation` ganha um item fixo "Documentos" (`/documentos`), ao lado de Mural,
incondicional — não é recurso de demonstração.

### Novo em `IntranetErrors`

```
Colecoes.TituloRequired
Colecoes.TituloTooLong(limit)
Colecoes.NotFound     -- inexistente, arquivada ou fora do alcance: mesmo erro
```

## Impacto no que já existe

O construtor de `Documento` muda de assinatura — `visibilidade` vira `Visibilidade?`, entra
`colecaoId`. `PublicarDocumentoHandler`, `DocumentoDto` (ganha `ColecaoId` e `Visibilidade`
nullable) e os testes atuais de publicação precisam de ajuste correspondente. Migration nos dois
providers altera `ie_visibilidade` para nullable — sem risco de dado existente, porque todo
documento hoje é avulso e já tem o campo preenchido.

## Testes

| Caso | Esperado |
|---|---|
| `Documento` com `ColecaoId` e `Visibilidade` preenchidos | `DomainInvariantException` |
| `Documento` sem `ColecaoId` e sem `Visibilidade` | `DomainInvariantException` |
| Coleção `Setor` | invisível para quem não pertence ao setor |
| Coleção `Empresa` | visível a todos, mesmo que o setor dono seja de acesso restrito |
| Documento dentro de coleção `Empresa` | visível independente de qualquer visibilidade própria — ela não existe |
| Coleção arquivada | some da vitrine; continua na aba do setor administrador |
| Coleção inexistente / fora do alcance | mesmo erro no `Detalhe` |
| Isolamento de tenant | mesmo padrão dos testes já existentes de Documento e Publicação |

## Fora de escopo

Ver seção "O que ficou de fora, e por quê", acima.
