# Menu principal extensível e ordenável — desenho do recurso

**Data:** 2026-10-29
**Estado:** aprovado, implementado
**Origem:** issue #2 ("Ponto de extensão para itens fixos extras no menu principal"), ampliada na conversa de desenho com a ordenação pelo admin

## Problema

Os itens fixos do grupo principal do menu (Mural e Diretório) estão escritos à mão em
`IntranetNavigation.Build`, e a decisão de exibi-los vem de flags do `NavigationRequest`. Quem adota a
intranet e quer uma página própria no menu, fora da árvore de algum setor, precisa editar
`IntranetNavigation.cs` e `NavigationViewComponent.cs`. Num fork, isso vira conflito a cada
sincronização. A página em si já pode morar numa RCL separada, porque o `Secco.Intranet.Web`
referencia o projeto e o MVC descobre os controllers dela. **O único bloqueio é o item de menu.**

Um item `Personalizado` na árvore de um setor não resolve: ele aparece debaixo do setor, na seção
"Setores", e a página não pertence a nenhum setor.

Um segundo problema surgiu junto. A ordem do menu é fixa no código: o grupo principal segue a ordem
em que os itens são escritos, e os setores vêm em ordem alfabética. O admin da instalação não tem
como dizer "a página Institucional vem antes do Diretório" nem "o setor Comercial vem primeiro".

## Objetivos

1. Um projeto externo, só por estar referenciado pelo `Secco.Intranet.Web`, acrescenta itens ao
   grupo principal, sem editar `IntranetNavigation.cs`, `NavigationViewComponent.cs` nem `Program.cs`.
2. Cada item extra tem um gate de visibilidade próprio, respeitado no menu.
3. O `intranet-admin` define a ordem do grupo principal (fixos e extras) e da seção Setores, por
   tenant.
4. Enquanto ninguém reordenar, o menu fica exatamente como é hoje.

## Fora de escopo

- Ordenar o grupo Administração. Ele continua fixo (Setores, Acesso, Inventário e o novo Menu).
- Mover um item de um grupo para outro: a página extra não vai para a seção Setores, e vice-versa.
- Item extra com filhos (submenu). O item extra é uma folha.
- Item extra apontando para URL externa (`https://...`). Só caminho local.
- Item extra em grupo novo ou no grupo Administração.
- Ordem por usuário. A ordem é do tenant e vale para todos.

## Anti-objetivos

- O tema não pode mudar. Ele continua recebendo uma `NavigationModel` já resolvida (ADR-0004), e
  nenhum parcial novo entra no contrato.
- Item extra quebrado não pode derrubar a página. O menu está no layout de todas as telas, inclusive
  na de erro: quem falha some do menu e deixa um aviso no log.
- Reordenar não muda quem vê o quê. A ordem só decide a posição entre os itens que o usuário já
  enxerga.

## Desenho

### 1. Contrato: projeto `Secco.Intranet.Web.Extensibility`

Projeto novo, pequeno, sem referência a nenhum outro projeto do produto. É o mesmo motivo da
ADR-0004: o projeto externo não pode referenciar o `Secco.Intranet.Web`, porque é o Web que
referencia ele. Depende só do BCL e de `Microsoft.Extensions.DependencyInjection.Abstractions`.

```csharp
public interface IItemDeMenuPrincipal
{
	string Chave { get; }     // identidade estável; é o que o banco guarda para lembrar a posição
	string Rotulo { get; }
	string? Icone { get; }    // classe do Bootstrap Icons (bi-*); nulo = sem ícone
	string Url { get; }       // caminho local, começando com "/"
	int Ordem { get; }        // posição padrão entre os extras, até o admin reordenar
	Task<bool> VisivelParaAsync(ClaimsPrincipal usuario, CancellationToken cancellationToken);
}
```

O `CancellationToken` no gate é um acréscimo à proposta da issue, porque todos os vizinhos assíncronos
do produto o recebem.

Regras do item, conferidas pelo core:

- `Chave`: de 1 a 64 caracteres, só `[a-z0-9-]`. `mural` e `diretorio` são reservadas aos fixos.
- `Url`: começa com `/` e não com `//`.
- Duas implementações com a mesma `Chave`: vale a primeira em ordem de nome completo do tipo, e a
  outra é ignorada.
- Item que viola alguma regra fica fora do menu e da tela de ordenação, com um `LogWarning` que
  nomeia o tipo e a regra. *(decisão de desenho: falhar no startup seria mais barulhento, mas o menu
  já segue a regra de "some e avisa" para setor com slug reservado)*

O projeto também expõe `services.AddItemDeMenuPrincipal<T>()` para registro explícito pelo DI.

### 2. Descoberta

Um catálogo singleton no Web (`CatalogoDeItensDoMenuPrincipal`) lê o `ApplicationPartManager` na
primeira vez que é usado. Ele junta os tipos concretos e públicos que implementam
`IItemDeMenuPrincipal` nas `AssemblyPart`s com os registrados pelo `AddItemDeMenuPrincipal<T>()`, sem
repetir tipo. A cada requisição, os itens são instanciados no escopo dela com
`ActivatorUtilities`, então podem injetar serviços scoped (`IAuthorizationService`, um handler).

A leitura é feita no primeiro uso, e não no registro, de propósito: uma `AssemblyPart` acrescentada
depois do `AddControllersWithViews` também é enxergada, e é assim que os testes de integração
simulam o projeto externo.

O `Program.cs` do core ganha uma linha (registrar o catálogo). Projeto externo não edita nada.

### 3. Montagem do menu

O `NavigationViewComponent`:

1. pede ao catálogo os itens do escopo atual;
2. avalia o gate de cada um. No modo aberto de DEV o gate não é chamado e todos ficam visíveis,
   como os outros itens. Um gate que lança exceção conta como invisível, com `LogWarning`;
3. carrega as posições salvas do tenant (com o mesmo `try/catch` dos setores: sem tenant ou com
   falha, sem posições, e vale a ordem padrão);
4. passa no `NavigationRequest` os extras visíveis e as posições.

O `IntranetNavigation.Build` monta o grupo principal com os fixos visíveis (Mural sempre, Diretório
quando `MostrarDiretorio`) e os extras visíveis, e ordena (seção 4). `Ativo` de um extra usa o
mesmo `Corresponde` dos outros itens, comparando com a parte de caminho da `Url` (sem query string,
sem `/` final). Os setores são ordenados pela mesma regra.

### 4. Regra de ordenação

Vale igual para o grupo principal e para a seção Setores, e mora numa função pura da Application,
usada tanto pelo menu quanto pela tela de ordenação, para que os dois nunca discordem:

1. itens **com** posição salva vêm primeiro, pela posição;
2. itens **sem** posição salva vêm depois, na ordem padrão do grupo;
3. posição salva de item que não existe mais (setor excluído ou desativado, projeto externo
   removido) é ignorada.

Ordem padrão:

- **Grupo principal:** Mural, Diretório, e então os extras por `Ordem` e, no empate, por `Rotulo`
  (ordinal, sem diferenciar caixa).
- **Setores:** por `Nome`, como hoje.

Chave de cada item: `mural`, `diretorio`, a `Chave` do extra; para setor, o `Id` em formato `D`.

### 5. Persistência

Entidade `PosicaoMenu` no domínio (`Secco.Intranet.Domain.Menu`), tabela `tb_posicoes_menu` pela
convention (ADR-0017):

| Propriedade | Coluna | Regra |
|---|---|---|
| `Id` (Guid) | `id_pk_posicao_menu` | |
| `Grupo` (`GrupoDoMenu`: `Principal = 1`, `Setores = 2`) | `ie_grupo` | |
| `Chave` (string, 64) | `ds_chave` | |
| `Ordem` (int) | `nr_ordem` | |

Índice único `uk_posicoes_menu_ie_grupo_ds_chave`. Migration nos dois engines, com o mesmo nome.

Toda gravação reescreve o grupo inteiro: as chaves daquele grupo, na ordem efetiva atual, viram as
linhas `0..n-1`, e as órfãs saem. Pelo mesmo motivo do `MoverItemMenuHandler`, trocar só dois
valores de `Ordem` não basta. Por isso, depois do primeiro movimento, todo item existente tem
posição, e só item novo cai na regra 2 da seção 4.

### 6. Tela de ordenação

- Rota `/menu`, controller `MenuController` com `[SomenteIntranetAdmin]` (ADR-0008).
- Item "Menu" (`bi-list-ol`) no grupo Administração, visível com `MostrarAdministracao`, depois de
  Acesso.
- `menu` entra em `SlugsReservados`.
- A página mostra dois cards, "Principal" e "Setores", cada um com a lista na ordem efetiva e os
  botões subir e descer por linha (POST com antiforgery, como os da árvore do setor). Primeiro não
  sobe e último não desce, e o botão sai desabilitado.
- "Principal" lista Mural, Diretório e **todos** os extras válidos registrados, sem avaliar o gate:
  o admin ordena o que existe, não só o que ele enxerga. "Setores" lista os setores ativos, todos.
- Botão "Restaurar padrão" por card, com confirmação: apaga as posições daquele grupo.
- Composta só com os parciais do contrato de tema (`_PageHeader`, `_Card`, `_EmptyState`), nos dois
  temas e nos dois modos de cor (skill `secco-intranet-temas`).
- Mensagem de retorno no padrão do `VoltarParaMenu` do `SetoresController`.

### 7. Auditoria

Recurso `menu` (já existe). Verbos novos:

- `menu.ordem-mover`: payload `{ grupo, chave, de, para }`;
- `menu.ordem-restaurar`: payload `{ grupo }`.

### 8. Documentação e ADR

- **ADR-0011:** ponto de extensão do menu principal em projeto próprio (`Secco.Intranet.Web.Extensibility`),
  descoberto pelas application parts. A regra do índice de ADRs pede uma, porque a decisão afeta a
  extensibilidade por terceiros.
- **`docs/extensao.md`**, um guia novo, com link a partir de `docs/temas.md`. Mostra como criar a
  RCL, implementar o item, referenciar o projeto no Web, e as regras de `Chave`/`Url`. Avisa também
  que a primeira parte da `Url` não deve coincidir com o slug de um setor existente, porque a rota
  da RCL e a página do setor disputariam o caminho.
- **`docs/roadmap.md`**: o item entregue.

## Critérios de aceite

- [ ] Um projeto externo, só por estar referenciado, faz o item aparecer no grupo principal, sem
      editar `IntranetNavigation.cs`, `NavigationViewComponent.cs` nem `Program.cs`.
- [ ] O gate é respeitado: item invisível para quem não passa nele, e visível no modo aberto de DEV.
- [ ] Um gate que lança exceção some do menu, e a página renderiza.
- [ ] O item fica ativo quando a rota atual corresponde à URL dele.
- [ ] Sem posição salva, o menu é idêntico ao de hoje.
- [ ] O `intranet-admin` sobe e desce itens do grupo principal (fixos inclusive) e setores; o menu de
      todos os usuários do tenant reflete a ordem; outro tenant não é afetado.
- [ ] Item ou setor novo, depois de uma reordenação, aparece no fim do grupo, na ordem padrão.
- [ ] "Restaurar padrão" volta o grupo à ordem padrão.
- [ ] Quem não é `intranet-admin` recebe 403 em `/menu`.
- [ ] Testes unitários de `IntranetNavigation` e da regra de ordenação cobrem ordem, item ativo,
      posição órfã e item sem posição. Teste de integração cobre descoberta, gate e a tela.
- [ ] ADR-0011, `docs/extensao.md` e roadmap atualizados.

## Decisões que ficam com quem implementar

- Nomes dos handlers, DTOs e view models, seguindo os vizinhos de `Application/Menu`.
- Como o teste de integração registra a `AssemblyPart` do item de exemplo (factory dedicada ou
  `ConfigureApplicationPartManager` na factory existente).
- O texto exato das mensagens de retorno e da confirmação.
