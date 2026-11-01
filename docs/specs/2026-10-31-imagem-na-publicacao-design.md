# Imagem na publicação do Mural — desenho do recurso

**Data:** 2026-10-31
**Estado:** aprovado, implementado
**Origem:** conversa de 2026-10-31, depois de publicar um aviso com imagem por endereço externo
para ver como ficava

## Problema

Quem publica no Mural quer o aviso visualmente bonito. Muitas vezes a pessoa monta a arte inteira
fora da intranet, como um cartaz ou flyer, e publica só a imagem, com o título e no máximo uma
linha de texto. Em outros casos o aviso é texto com uma imagem para ilustrar.

Hoje não há como fazer isso direito:

- **O corpo é markdown e aceita `![](endereço)`, mas a imagem precisa já estar publicada em algum
  lugar.** Quem tem a arte no computador não tem onde pô-la.
- **O botão de imagem do editor engana.** Ele aceita enviar um arquivo, mas embute a imagem em
  base64 dentro do texto. Qualquer foto passa do limite de 4000 caracteres do corpo, e a gravação é
  recusada com "O corpo excede o limite de 4000 caracteres", sem dizer que a causa é a imagem.
- **O texto é obrigatório.** Um aviso que é só arte precisaria de um texto qualquer para gravar.

## Objetivos

1. Quem escreve no setor anexa **uma imagem** ao aviso, enviando o arquivo do computador.
2. A imagem aparece **inteira, na largura do card, depois do texto**, no Mural e na página da
   publicação.
3. **Só o título é obrigatório.** Texto e imagem são opcionais: o aviso pode ser só título e
   imagem, só título e texto, ou só o título.
4. A imagem segue a **mesma regra de quem vê** da publicação.

## Fora de escopo

- Mais de uma imagem por publicação.
- Imagem no meio do texto (posição escolhida pelo autor).
- Imagem no e-mail ou no sino. As notificações continuam com título, resumo do texto e link.
- Galeria, carrossel, clique para ampliar.
- GIF animado e vídeo.
- Recorte ou edição da imagem dentro da intranet.
- Imagem em Documentos ou em outras telas.

## Anti-objetivos

- **A arte não pode perder qualidade visível.** Ela é o aviso: texto pequeno dentro do flyer
  precisa continuar legível. Nada de miniatura nem recorte.
- **Quem não pode ver o aviso não vê a imagem**, nem pelo endereço direto.
- **Nenhum caminho deixa a imagem dentro do texto.** O limite de 4000 caracteres continua valendo
  para texto.

## Desenho

### 1. O que a pessoa faz

No formulário de aviso do setor (`/{slug}/avisos`), abaixo do editor:

- campo **"Imagem"** (arquivo, opcional), com a ajuda "PNG, JPG ou WebP, até 10 MB. Aparece no fim
  do aviso, na largura do card.";
- campo **"Descrição da imagem"** (texto curto, opcional), com a ajuda "Para quem usa leitor de
  tela. Se a imagem é o aviso inteiro, escreva o que ela diz.";
- na edição de um aviso que já tem imagem: a miniatura atual, a descrição atual e a caixa
  **"Remover a imagem"**. Enviar arquivo novo troca a imagem.

O botão de imagem do editor de markdown sai da barra nos dois temas, e o colar de imagem no
editor fica desligado. A imagem entra só pelo campo próprio.

### 2. Regra do conteúdo

- **Título obrigatório**, como hoje.
- **Texto opcional** (decisão do usuário). Deixa de existir o `CorpoRequired` e a invariante "corpo
  não vazio" da `Publicacao`; o corpo vazio é gravado como texto vazio. O limite de 4000 caracteres
  continua.
- **Imagem opcional.** Um aviso só com título é válido.

### 3. Processamento

- Pela `Secco.SDK.Imaging`, já usada na foto do Diretório (ADR-0042 da plataforma): entra JPEG, PNG
  ou WebP; sai **WebP**, com o maior lado limitado a **2048 px** e sem ampliar. Uma arte de
  1080×1350 fica como está; uma foto de câmera de 4000 px desce para 2048. A transparência do PNG é
  preservada.
- Reprocessar descarta os metadados do arquivo original (EXIF, localização).
- Limites de defesa da SDK (`Intranet:Imaging`): 10 MB de entrada e o máximo de pixels padrão.
  Arquivo fora dos limites, formato não suportado ou imagem ilegível viram erro de validação no
  formulário, com mensagem própria, nunca 500.
- O processamento acontece **antes** de qualquer gravação. Imagem recusada não grava nada.
- Se o formulário volta com erro em outro campo, o arquivo escolhido não volta junto (o navegador
  não permite). A mensagem pede para escolher a imagem de novo. *(decisão de desenho)*
- Ordem de gravação igual à de Documentos: arquivo no armazenamento, depois a linha. Uma falha
  entre os dois deixa um arquivo cifrado órfão, inerte.

### 4. Armazenamento

- O arquivo processado vai para o `IArquivoStore` cifrado em envelope, como Documentos e a foto do
  Diretório, na pasta lógica `mural/imagens`.
- A `Publicacao` ganha `ImagemCaminhoRelativo`, `ImagemChaveEmbrulhada` (as duas nulas juntas) e
  `ImagemDescricao` (até 300 caracteres, nula quando vazia). Nomes de coluna pela convention
  (ADR-0017). Migration `PublicacaoImagem` nos dois engines.
- Trocar ou remover a imagem apaga o arquivo anterior do armazenamento, depois de gravar a
  publicação. Não há histórico de imagens.
- Arquivar a publicação não apaga a imagem, pelo mesmo motivo de Documentos: arquivar é deixar de
  publicar, não apagar.

### 5. Como é servida

- Endpoint `GET /publicacoes/{id}/imagem`.
- Quem lê: a mesma regra da página da publicação (`ObterPublicacaoHandler`): no ar, e `Empresa` ou
  setor do leitor. **Além disso, quem escreve no setor dono vê a imagem de qualquer publicação
  ativa dele, mesmo agendada ou expirada**, porque a lista de avisos do setor mostra a miniatura
  desses avisos ao autor. Fora disso, ou sem imagem, **404**, igual para inexistente e para fora do
  alcance.
- Mesmos cabeçalhos da foto do Diretório: `Content-Type: image/webp`, `private, no-cache`, `ETag`
  derivado do caminho do arquivo (muda quando a imagem é trocada), `nosniff`, e `304` quando o
  `If-None-Match` confere.
- Arquivo ausente ou corrompido no armazenamento vira 404 com aviso no log, não 500.

### 6. Exibição e contrato de tema

- `CardModel` (contrato de tema, ADR-0004) ganha `ImagemModel? Imagem`, com `Url` e `Descricao`. O
  tema desenha a imagem **depois do corpo**, na largura do card, com o raio do tema, e
  `alt` = descrição, ou vazio quando não há.
- `Vertical` e `Horizontal` implementam no `_Card`, claro e escuro (skill `secco-intranet-temas`).
- A página da publicação (`Mural/Detalhe`), que não usa o `_Card`, mostra a imagem depois do corpo
  com a mesma classe.
- Aviso sem texto: o card mostra título, badge do setor, meta e a imagem (se houver), sem bloco de
  texto vazio.
- A lista de avisos do setor (`/{slug}/avisos`) mostra uma miniatura pequena ao lado dos avisos
  que têm imagem, para o autor reconhecer qual é qual.
- `docs/temas.md` documenta o campo novo do `CardModel`.

### 7. Notificações

Sem mudança de canal. O resumo do sino e do e-mail continua vindo do texto. Num aviso sem texto o
resumo fica vazio, e a mensagem vai com título e link para a publicação.

### 8. Auditoria

Os verbos de publicar e editar já existentes ganham no payload `imagem` (`true`/`false`) e, na
edição, `imagemAcao` (`manter`, `trocar`, `remover`).

## Critérios de aceite

- [ ] Publicar aviso com imagem PNG, JPG ou WebP: a imagem aparece no fim do card, no Mural e na
      página da publicação, nos dois temas, claro e escuro.
- [ ] Só o título é obrigatório: aviso só com título, só com título e imagem, ou só com título e
      texto é aceito; sem título é recusado.
- [ ] Quem escreve no setor vê a miniatura de um aviso agendado na lista do setor; quem só lê
      recebe 404 na imagem desse aviso até ele entrar no ar.
- [ ] Imagem de 4000 px é guardada com 2048 px no maior lado; uma de 1080 px fica com 1080 px.
- [ ] Arquivo acima de 10 MB, formato não suportado ou arquivo que não é imagem: mensagem de erro no
      formulário e nada gravado.
- [ ] Aviso "somente este setor": quem não é do setor recebe 404 em `/publicacoes/{id}/imagem`.
- [ ] Editar: trocar a imagem muda o `ETag` e apaga o arquivo anterior; remover tira a imagem do
      card.
- [ ] O botão de imagem não aparece na barra do editor nos dois temas.
- [ ] `alt` da imagem é a descrição informada.
- [ ] Migration nos dois engines; `docs/temas.md` atualizado.

## Decisões que ficam com quem implementar

- Nome do handler e se a imagem entra pelo `PublicarPublicacaoHandler`/`EditarPublicacaoHandler`
  existentes ou por um caso de uso próprio chamado por eles.
- Onde mora o processamento da imagem: um método novo no `IProcessadorDeImagem` ou uma porta
  irmã.
- Como desligar o botão de imagem e o colar no Toast UI (opção `toolbarItems`, hook de imagem).
- O tamanho exato da miniatura na lista de avisos do setor.
