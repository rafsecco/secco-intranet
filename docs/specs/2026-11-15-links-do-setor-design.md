# Links do setor — design

**Estado:** entregue em 2026-11-16.

Primeiro dos três projetos que saíram do item "Ferramentas do dia a dia" do [roadmap](../roadmap.md). Os outros dois, Reserva de sala e o motor de processos, têm conversa e spec próprias, nessa ordem.

## Por quê

Cada setor usa no dia a dia um conjunto de sites e sistemas (ERP, banco, portais de governo). O setor passa a manter esses endereços no próprio menu da intranet, em grupos, e clicar leva direto ao site.

O menu do setor já sabe mostrar isso: um item "Personalizado" sem endereço agrupa, e um com endereço vira link. Faltam três coisas:

1. Só o `intranet-admin` mexe no menu. O setor não consegue cadastrar um link sozinho.
2. Não há como escolher se o link abre na mesma página ou em uma nova.
3. A tela de menu é genérica (slug, tipo, rota, ícone, nível de acesso), com mais campos do que um link precisa.

## Objetivos

- **Links é um recurso do setor**, como Documentos, Avisos e Vencimentos: um item "Links" no menu do setor.
- Os links aparecem **no próprio menu**, em grupos: Financeiro › Links › Bancos › Internet Banking. Não existe página de links para quem lê.
- **Quem tem escrita no setor** adiciona, edita, organiza e exclui grupos e links, sem depender do `intranet-admin`.
- Cada link escolhe se abre **na mesma página ou em uma nova**.

## Fora do escopo

- Página com a lista de links para quem só lê.
- Links pessoais de cada usuário, ou links da empresa inteira fora dos setores.
- Ícone ou imagem própria por link.
- Nível de acesso por link (todos seguem a leitura do setor).
- Verificar se o endereço responde, ou contar cliques.

## O que não pode acontecer

- Quem tem escrita no setor alterar, pela tela de links, qualquer item de menu **fora do trecho abaixo de "Links"** (Documentos, Avisos, Vencimentos, itens personalizados do admin) ou de outro setor.
- Um link levar a um endereço que não seja `http`, `https` ou um caminho da própria intranet.
- A mudança alterar o que o `intranet-admin` já faz na tela de menu do setor.

## Como fica para quem usa

No menu, para quem lê o setor:

```
Financeiro
 ├ Avisos
 ├ Documentos
 └ Links
     ├ Bancos
     │   └ Internet Banking      (abre em nova aba)
     ├ Sistemas internos
     │   └ ERP                   (abre na mesma página)
     └ Calendário fiscal         (link sem grupo)
```

Quem tem escrita vê, além disso, a entrada **Gerenciar links** no fim do submenu Links. Ela abre a única tela do recurso:

```
Financeiro › Gerenciar links               [ + Grupo ]  [ + Link ]

Bancos                                     ↑ ↓  editar  excluir
   Internet Banking     nova aba           ↑ ↓  editar  excluir
Sistemas internos                          ↑ ↓  editar  excluir
   ERP                  mesma página       ↑ ↓  editar  excluir
Calendário fiscal       nova aba           ↑ ↓  editar  excluir
```

Regras:

| Assunto | Regra |
|---|---|
| Link | Nome, endereço, grupo (opcional) e onde abre: mesma página ou nova |
| Grupo | Só o nome |
| Endereço | `http` ou `https` absoluto, ou caminho da própria intranet começando com `/`; mesma validação que o menu já aplica |
| Onde abre | "Nova página" vem marcado em todo cadastro novo, de link do setor ou de item personalizado do admin; a pessoa pode desmarcar. Item que já existia continua abrindo na mesma página |
| Níveis | Um nível de grupo abaixo de Links; grupo não entra em grupo |
| Link sem grupo | Permitido, direto abaixo de Links |
| Ordem | Subir e descer entre os irmãos; ao editar um link dá para trocá-lo de grupo |
| Excluir | Apaga o item, como o menu faz hoje. Grupo com links dentro precisa ser esvaziado antes |
| Grupo vazio | Não aparece no menu para quem lê; aparece na tela de gerenciar |
| Links vazio | Quem lê não vê o item "Links"; quem escreve vê, só com "Gerenciar links" |

## Abordagens

1. **Um nó "Links" na árvore do menu, com tela própria para o setor (escolhida).** Reaproveita a árvore, a exibição e as regras do menu. O setor só alcança o que está abaixo de "Links". Custo: uma tela nova e a trava que limita o alcance.
2. **Liberar a tela de menu do setor para quem tem escrita.** Quase sem código novo, mas daria ao setor poder de remover Documentos, Avisos e Vencimentos e de mexer em nível de acesso.
3. **Tabela separada de links, desenhada no menu ao exibir.** Mantém links fora dos itens de menu, mas cria uma segunda árvore e faz o menu ser montado de duas fontes.

Rafael escolheu a 1.

## Como é construído

| Parte | O que entra |
|---|---|
| Menu | Novo `TipoDeItemMenu.Links`, no máximo um por setor, slug `links`, sem rota. É o único tipo embutido que aceita filhos; os filhos são itens `Personalizado` (sem rota = grupo, com rota = link) |
| Item de menu | Novo campo "abre em nova página" no `ItemMenu`, falso por padrão. Vale para qualquer item com rota, inclusive os que o admin cria na tela de menu, que ganha a mesma caixa de seleção |
| Banco | Uma coluna nova na tabela de itens de menu, nome pela ADR-0017, migration em par (SQL Server e PostgreSQL) |
| Exibição | `NavigationItemModel` ganha `NovaAba`. Os dois temas passam a escrever `target="_blank" rel="noopener noreferrer"` quando ele é verdadeiro. Para quem escreve no setor, a navegação acrescenta "Gerenciar links" como último filho do nó Links, mesmo que ele esteja vazio |
| Aplicação | Casos de uso próprios de links (listar o trecho, criar grupo, criar link, editar, mover, excluir), que recebem o setor e só agem em item que seja descendente do nó Links daquele setor. Os handlers atuais do menu, que agem por `id` e são só do admin, não mudam de regra |
| Web | `LinksController` em `/{slug}/links`, só com a tela de gerenciar e seus POSTs, todos com antiforgery. Sem escrita no setor, ou sem o nó Links no menu, responde 404 |
| Criar setor | A tela de novo setor ganha "Habilitar links", ao lado das opções de Documentos, Avisos e Vencimentos |
| Auditoria | Os verbos de menu que já existem (criar, mover, excluir item) mais um de edição, com o ator sendo quem tem escrita no setor |

**A trava de alcance** é a peça de segurança do projeto. Cada caso de uso carrega a árvore do setor da URL, acha o nó Links e confere que o item alvo (e o grupo de destino, quando há) é descendente dele. Item de outro setor, de fora do trecho, ou o próprio nó Links respondem como não encontrado.

**Slug dos itens.** O menu exige slug único entre irmãos, mas links e grupos não têm página própria. O slug é gerado do nome, com sufixo numérico quando repete, e a pessoa nunca o vê.

**Endereço seguro.** A validação ao gravar é a que o menu já tem (`http`, `https` ou caminho local que não comece com `//`). O `rel="noopener noreferrer"` impede que a página aberta em nova aba alcance a janela da intranet.

## Decisões minhas, para conferir

- **Setor existente não ganha Links sozinho.** O admin adiciona pela tela de menu do setor, onde "Links" passa a aparecer como tipo disponível. Setor novo ganha se "Habilitar links" estiver marcado, e a caixa vem marcada.
- **"Abre em nova página" vale para o menu inteiro**, não só para os links do setor: o campo fica no item de menu, e o admin também pode usá-lo nos itens personalizados dele.
- **O nó "Links" não leva a lugar nenhum ao clicar**: só abre o submenu, como os grupos.
- **Excluir apaga de vez**, sem arquivar, porque é o que o menu já faz e não há conteúdo a preservar além do endereço. A auditoria guarda o que foi excluído.
- **Sem limite de quantidade** de grupos e links. Menu com dezenas de itens fica ruim de usar, mas isso é escolha de quem cadastra.

## Quebras a registrar no CHANGELOG

- Migration nova na tabela de itens de menu: rodar o `migrate` antes de subir a versão.
- Tema próprio (fork): o contrato `NavigationItemModel` ganha `NovaAba`. Tema que não escrever o `target` continua funcionando, mas abre tudo na mesma página.

## Testes

| Tipo | O que cobre |
|---|---|
| Unidade | Trava de alcance (item fora do trecho, de outro setor, o próprio nó Links); um nível de grupo; grupo com filhos não exclui; geração de slug com repetição; padrão de "nova página" |
| Integração | Escrita exigida em toda ação; 404 sem o nó Links; antiforgery; menu de quem lê e de quem escreve (com e sem links, grupo vazio); `target` e `rel` no HTML dos dois temas; tela de menu do admin continua igual; migrations nos dois bancos; isolamento entre tenants |
| Tela (opt-in) | Criar grupo e link pela tela, ver no menu, clicar abre nova aba; organizar; CSP sem violação; dois temas |

## O que fica para quem implementa

- Nomes exatos de classes, rotas dos POSTs e da coluna, dentro das convenções do repositório.
- Formulários na própria tela ou em páginas separadas de criar e editar, o que ficar mais simples sem script inline (a CSP não permite).
- Texto das mensagens e o ícone padrão do nó Links.
