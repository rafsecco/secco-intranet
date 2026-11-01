# Editar documento e substituir arquivo — desenho do recurso

**Data:** 2026-11-01
**Estado:** proposta, aguardando aprovação
**Origem:** issue #4 ("Bibliotecas de documentos"), parte 2 de 3. A parte 1 (hierarquia e ordem
manual) está em [2026-10-31-documentos-hierarquia-e-ordem-design.md](2026-10-31-documentos-hierarquia-e-ordem-design.md);
a parte 3 são as bibliotecas (dono sem setor), que reaproveitam o que esta parte entrega.

## Problema

Um documento publicado não tem edição. Título, descrição, visibilidade e arquivo ficam como foram
enviados. Para corrigir um erro de digitação no título ou publicar a revisão de uma política, o
editor arquiva o documento e publica outro. Com isso o item perde a posição na lista, e os anexos
pendurados debaixo dele impedem o arquivamento até serem movidos.

## Mudança em relação à issue #4

A issue pedia que a substituição **guardasse a versão anterior**, consultável só pelo admin. A
decisão desta spec é outra: **substituir troca o arquivo de vez, e o anterior é apagado do
armazenamento**. O motivo é não acumular arquivos que ninguém lê e que exigem manutenção de espaço
em disco.

Quem precisa preservar uma versão publica outro documento, por exemplo "Política de Treinamento
(v2)", e decide o que fazer com o anterior. O critério de aceite "guarda a versão anterior" da
issue sai.

## Objetivos

1. Quem escreve no setor edita um documento no ar: título, descrição, visibilidade e, se quiser,
   o arquivo.
2. Quem escreve no setor edita o título e a descrição de um grupo.
3. A edição preserva a posição do item e os itens abaixo dele.
4. Trocar o arquivo não deixa nada para trás no disco, salvo falha (ver anti-objetivos).

## Fora de escopo

- Histórico de versões, consulta ou restauração de conteúdo anterior.
- Editar documento arquivado.
- Mudar o tipo do item (documento vira grupo ou o contrário).
- Mover o item para outro setor.
- Notificação a quem lê. Publicar documento já não notifica hoje, e editar segue igual.
- Bibliotecas (parte 3).

## Anti-objetivos

- **Nunca perder o arquivo novo por causa do antigo.** Se apagar o anterior falhar, a edição
  continua valendo, e sobra só um arquivo órfão, cifrado e sem chave conhecida no banco.
- **Nunca deixar o documento apontando para um arquivo que não existe.** O arquivo novo é gravado
  antes de o registro mudar.
- **A regra de quem vê não muda.** Quem lê, quem escreve e o 404 indistinto da ADR-0005 continuam
  como estão.

## Desenho

### 1. Quem edita

A mesma regra de "Organizar documentos" e de arquivar: `setor-{id}:write`, com o `intranet-admin`
sempre autorizado, e o item de menu Documentos ativo no setor (desligar o recurso fecha a leitura
**e** a escrita dele). Sem permissão, com o recurso desligado, com documento de outro setor,
arquivado ou inexistente, a tela e o envio devolvem **404**, o mesmo das rotas de organizar e
arquivar. Não distinguir esses casos é a regra da ADR-0005.

### 2. O que muda

| Item | Campos editáveis |
|---|---|
| Documento | título, descrição, visibilidade (`Setor`/`Empresa`) e arquivo |
| Grupo | título e descrição. A visibilidade de grupo continua sempre `Setor` |

- **Arquivo opcional na edição.** Em branco, o arquivo atual fica. Enviado, substitui o atual.
- **Mesmas regras do envio de hoje** para o arquivo novo: extensões aceitas
  (`Intranet:Documentos:ExtensoesPermitidas`), tipo apurado pelos primeiros bytes e nunca pelo
  `Content-Type` do cliente, e o limite `Intranet:Documentos:TamanhoMaximoBytes`. O arquivo novo
  pode ter outra extensão (um `.docx` no lugar de um `.pdf`).
- **Mesmos limites de título e descrição** da publicação.
- **Não mudam:** posição (`ParentId`, `Ordem`), filhos, setor dono, tipo, autor (`CriadoPor`) e
  data de criação.

O `Documento` ganha os métodos que faltam para isso (hoje há só `AlterarVisibilidade`). Os campos de
arquivo (`NomeArquivo`, `ContentType`, `Tamanho`, `CaminhoRelativo`, `ChaveEmbrulhada`) mudam
**juntos**, num único método, para que nunca fiquem misturados entre o arquivo antigo e o novo. A
entidade recusa trocar arquivo de um grupo.

**Sem migration:** nenhuma coluna nova. As colunas de arquivo já existem e já aceitam nulo (grupo).

### 3. Trocar o arquivo

A ordem segue o padrão que a foto do Diretório (`FotoDoColaboradorHandler`) e a imagem do Mural
(`ImagemDaPublicacao`) já usam:

1. Valida tudo o que não depende de gravar: permissão, item ativo, tipo, campos, extensão, tamanho
   e assinatura.
2. Grava o arquivo novo pelo `IArquivoStore` (chave nova, caminho novo).
3. Atualiza o registro e salva.
4. Registra na trilha de auditoria.
5. Apaga o arquivo antigo com `IArquivoStore.RemoverAsync`. Uma falha aqui vira aviso no log, com
   o tipo da exceção e sem o caminho, e a edição continua valendo.

Se o passo 3 falhar, o arquivo gravado no passo 2 fica órfão, como acontece hoje na publicação. É
inerte: cifrado, sem chave no banco e sem endereço conhecido.

### 4. Link de download

O endereço continua `GET /documentos/{id}/download` e passa a entregar o arquivo novo. A resposta
já sai com `Cache-Control: private, no-store`, então o navegador não guarda a versão antiga. Quem
baixou antes fica com a cópia que tem.

### 5. Telas

- **Ponto de entrada:** botão "Editar" em cada linha da tela "Organizar documentos"
  (`/{slug}/documentos/organizar`), ao lado de "Arquivar".
- **Formulário:** página própria `GET /{slug}/documentos/{id}/editar`, já preenchida com os dados
  atuais e com o nome do arquivo atual visível. O envio vai para
  `POST /{slug}/documentos/{id}/editar`, com antiforgery e o mesmo limite de requisição da
  publicação (64 MB). Para um grupo, o formulário mostra só título e descrição.
- **Aviso antes de substituir:** se um arquivo novo foi escolhido, ao clicar em "Salvar" o usuário
  confirma "O arquivo atual "{nome}" será substituído e apagado. Não dá para desfazer. Continuar?".
  Cancelar não envia nada. Sem arquivo escolhido, não há pergunta: editar só título, descrição ou
  visibilidade salva direto.
- **Como o aviso funciona:** o contrato de tema já tem `<form data-confirmar="Pergunta?">`, que
  pergunta sempre. Ele passa a valer também em `<input type="file" data-confirmar="Pergunta?">`:
  o tema pergunta no envio do formulário só se esse campo tiver arquivo escolhido. Os temas
  `Vertical` e `Horizontal` implementam, e `docs/temas.md` registra. Sem JavaScript o formulário
  envia direto, como já acontece com o `data-confirmar` de hoje. *(decisão minha: reaproveita o
  mecanismo de confirmação que o produto já usa em vez de uma tela intermediária, que obrigaria a
  guardar o arquivo enviado enquanto o usuário decide.)*
- **Erro de validação:** a página volta com os campos preenchidos e a mensagem.
- **Sucesso:** volta para "Organizar documentos" com a mensagem de confirmação pelo `Feedback` do
  tema, como as outras ações da tela.
- O formulário é view do core, não parcial do contrato de tema.

### 6. Auditoria

Verbo novo `documento.editar` (`VerbosDeAuditoria.DocumentoEditar`), recurso `documento`. O
registro diz quem, quando e **quais campos mudaram**, nunca o conteúdo do arquivo:

- título e visibilidade: valor anterior e novo;
- descrição: só a indicação de que mudou;
- arquivo: nome e tamanho, anterior e novo.

### 7. Documentação

- `docs/temas.md`: `data-confirmar` no campo de arquivo, no trecho que já descreve o atributo.
- `docs/roadmap.md`: o item da hierarquia diz "faltam substituir arquivo com histórico e as
  bibliotecas". Passa a citar esta parte como entregue, sem o histórico.
- Esta spec registra a mudança em relação à issue #4. Comentar na issue é decisão do dono do
  repositório.

## Critérios de aceite

- [ ] Com `setor-{id}:write` (ou `intranet-admin`), o botão "Editar" aparece em cada linha de
      "Organizar documentos" e abre o formulário preenchido.
- [ ] Sem a permissão, com o recurso Documentos desligado, ou com documento de outro setor,
      arquivado ou inexistente, o formulário e o envio devolvem 404.
- [ ] Editar título, descrição e visibilidade de um documento sem enviar arquivo mantém o arquivo,
      a posição e os filhos.
- [ ] Enviar arquivo novo troca o conteúdo baixado pelo mesmo link, com nome e tipo novos, e
      apaga o arquivo antigo do armazenamento.
- [ ] Com arquivo novo escolhido, "Salvar" pede confirmação citando o arquivo atual, e cancelar não
      envia. Sem arquivo escolhido, não pergunta. Vale nos temas `Vertical` e `Horizontal`.
- [ ] Arquivo novo fora das regras (extensão, assinatura, tamanho) é recusado, e nada muda: nem o
      registro nem o arquivo atual.
- [ ] Falha ao apagar o arquivo antigo não desfaz a edição e gera aviso no log sem o caminho.
- [ ] Grupo edita só título e descrição; a entidade recusa arquivo em grupo.
- [ ] A edição gera `documento.editar` na trilha, com os campos que mudaram e sem conteúdo.
- [ ] Testes de unidade (entidade e handler, inclusive a ordem gravar → salvar → apagar) e de
      integração (rota, permissão, troca real no armazenamento e isolamento de tenant).

## Decisões que ficam com quem implementar

- **Envio sem nenhuma mudança** *(decisão minha)*: sucesso, sem registro na trilha.
- **Duas edições ao mesmo tempo no mesmo documento** *(decisão minha)*: vale a última. No pior caso
  sobra um arquivo órfão, o mesmo efeito de uma falha no passo 5. Não há trava de concorrência.
- **Download em andamento durante a troca** *(decisão minha)*: em Linux o arquivo aberto continua
  legível até o fim; em Windows o `RemoverAsync` pode falhar e o arquivo vira órfão. Os dois casos
  são aceitáveis pelo anti-objetivo de nunca perder o arquivo novo.
- Nome do handler e do comando, texto das mensagens e o desenho visual do formulário.
