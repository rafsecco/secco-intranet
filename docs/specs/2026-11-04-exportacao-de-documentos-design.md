# Exportação Excel/PDF para RCLs — desenho do recurso

**Data:** 2026-11-04
**Estado:** aprovado, implementado
**Origem:** issue #5 ("Adotar o Secco.SDK.Documents (ADR-0058)"). A decisão sobre a trilha sem LogStream
está registrada [num comentário da issue](https://github.com/rafsecco/secco-intranet/issues/5#issuecomment-6637701111).

## Problema

A plataforma entregou o `Secco.SDK.Documents` (ADR-0058 da plataforma, `sdk-documents/v0.1.0`), que gera
listagens em `.xlsx` e PDF tabular no servidor. A ADR deixa com o produto três obrigações que só o host
cumpre:

- **Registro:** o exportador só existe depois de `AddSeccoDocuments("<seção>")`. Uma RCL externa, que é
  como um fork corporativo acrescenta páginas sem editar o core (ADR-0011), não tem onde registrar
  serviços.
- **Trilha síncrona e fail-closed:** a exportação é registrada **antes** do envio, e o arquivo não sai
  se o registro falhar. A `ITrilhaDeAuditoria` de hoje faz o oposto de propósito: engole falha de
  infraestrutura para não derrubar a ação do usuário.
- **Limite de taxa por pessoa (`sub`)**, além da concorrência da SDK.

O caso que motiva: um relatório numa RCL de um fork corporativo, com dados pessoais, precisa sair em
Excel e PDF. A tela o fork já consegue fazer. A exportação, só com a intranet ligada ao SDK.

## Objetivos

1. `ISeccoDocumentExporter` resolve no host, com a seção `Intranet:Exportacao` validada na subida.
2. Uma **trilha de exportação** fail-closed, ao lado da trilha atual, que não muda.
3. Uma **política de rate limiting nomeada**, por `sub`, com `429` acima do limite.
4. Uma RCL exporta **só injetando**: referencia o `Secco.Intranet.Web.Extensibility` e o
   `Secco.SDK.Documents`, e não registra nada.
5. `docs/extensao.md` e `docs/guia-de-deploy.md` explicam como usar e como configurar.

## Fora de escopo

- **Exportar listagens do core** (Diretório, Inventário, documentos). Fica para quando houver pedido, já
  sobre esta base. Por isso nenhuma permissão `<recurso>:export` nasce no catálogo do core nesta entrega.
- **Parcial de tema para os botões de exportação** (item 5 da issue). Um parcial novo vira item do
  contrato do `Theming`, com implementação obrigatória nos dois temas publicados e em todo tema de
  adotante (`docs/temas.md`), e obrigaria a RCL a referenciar o `Theming`. A RCL usa um formulário
  comum (seção 5). O parcial fica para quando o primeiro relatório real mostrar o formato de botão que
  ele pede.
- **Exportação assíncrona.** Fora do v1 da própria ADR-0058.

## Anti-objetivos

- Uma exportação sair em HML/PRD sem registro na trilha, inclusive por LogStream esquecido na
  configuração.
- A trilha atual (`ITrilhaDeAuditoria`) passar a lançar ou a recusar: nenhum handler existente muda de
  comportamento.
- O `Secco.Intranet.Web.Extensibility` ganhar dependência de projeto do produto ou de pacote da
  plataforma.

## Desenho

### 1. Contrato no `Secco.Intranet.Web.Extensibility`

O projeto continua dependendo só de `Microsoft.AspNetCore.App` (ADR-0011). Ele ganha:

- **`ITrilhaDeExportacao`**, com um método que recebe a descrição da exportação e devolve se o
  documento pode ser enviado:
  - **recurso:** chave do que foi exportado, `[a-z0-9-]`, de 1 a 64 caracteres, mesma regra da `Chave`
    do item de menu. É fixa no código da RCL, nunca vem do usuário.
  - **formato:** `xlsx` ou `pdf`.
  - **linhas:** quantas saíram (`ExportedDocument.RowCount`).
  - **filtros:** pares nome/valor, os mesmos que a RCL passou ao `ExportRequest.Filters`.
- **Constante com o nome da política** de rate limiting (`"exportacao"`), para `[EnableRateLimiting]`.

Recurso fora da regra, ou filtros nulos, é erro de programador e lança `ArgumentException`, como pedido
inválido no SDK.

*Decisão minha:* tipos próprios e primitivos (string para o formato, record próprio para o filtro e
`bool` ou enum de dois valores no retorno), e não `ExportFormat`, `ExportFilter` nem o `Result` do
SharedKernel. Assim o `Extensibility` não referencia pacote nenhum, e o sync do fork não ganha
dependência transitiva. A conversão de `ExportFilter` para o tipo do contrato é uma linha na RCL. O
nome exato dos tipos fica com quem implementa.

### 2. Trilha de exportação no host

Camadas, na ordem em que a chamada passa:

1. **Web:** a implementação de `ITrilhaDeExportacao` monta um `RegistroDeAuditoria`:
   - `Verbo`: constante nova `exportacao.gerar` em `VerbosDeAuditoria`;
   - `Recurso`: constante nova `exportacao` em `RecursosDeAuditoria`;
   - `RecursoId`: o recurso informado pela RCL;
   - `Metadata`: JSON com `formato`, `linhas` e `filtros`, cada valor de filtro cortado em **200
     caracteres**, mesmo limite da exportação do LogStream. Nunca o conteúdo.

   *Decisão minha:* verbo fixo em vez de um verbo por relatório. Quem busca "toda exportação" filtra
   por um verbo só, e `RecursoId` diz qual relatório foi.
2. **Application:** porta nova de trilha obrigatória, que devolve `Result` em vez de engolir a falha.
   A `ITrilhaDeAuditoria` fica como está, com a garantia "nunca lança" documentada.
3. **Infrastructure:** três adapters, escolhidos no registro:

| Situação | Adapter | Resultado |
|---|---|---|
| `Intranet:Auditoria:LogStreamUrl` preenchida (qualquer ambiente) | LogStream | sucesso só quando o LogStream aceita; recusa, rede ou timeout viram falha com log `Warning` |
| URL vazia e ambiente `Development` ou `Testing` | aceita | sucesso, com log `Warning`: exportação não auditada porque o LogStream não está configurado |
| URL vazia em qualquer outro ambiente | recusa | falha, com log `Warning` |

- O critério de DEV é o **nome do ambiente** (`IHostEnvironment`), não só a URL vazia. Um HML com a URL
  esquecida cai na recusa. É o mesmo critério do modo aberto do SecureGate, estendido a `Testing`
  porque o host dos testes de integração roda nele.
- **Diferença deliberada:** a `ITrilhaDeAuditoria` escolhe a `TrilhaSilenciosa` só pela URL vazia, em
  qualquer ambiente. A trilha de exportação decide também pelo ambiente. Os dois critérios **não
  devem ser unificados**: a trilha atual é fail-open de propósito.
- **Sem usuário identificado:** o adapter do LogStream hoje pula o registro quando não há ator (modo
  aberto). Na trilha obrigatória, *decisão minha:* sem ator em `Development`/`Testing` aceita com
  `Warning`, e sem ator em outro ambiente recusa.
- **Cancelamento pedido pelo chamador** (`RequestAborted`) propaga como `OperationCanceledException`,
  como no adapter atual. Timeout do `HttpClient` é falha.
- A montagem do `CreateAuditEntryRequest` (ator, correlação, `OccurredAt`) é a mesma do adapter atual.
  Quem implementa decide se extrai ou compartilha esse trecho.

### 3. Registro do SDK e configuração

- `AddSeccoDocuments(<seção>)` no host, com os padrões do pacote (100 mil linhas no Excel, 10 mil no
  PDF, concorrência pela metade dos processadores). Valor fora da faixa derruba a subida.
- `Secco.SDK.Documents` entra no `Directory.Packages.props` na versão estável mais recente do feed,
  hoje `0.1.0`. Conferir ao vivo na hora de implementar.
- **Seção `Intranet:Exportacao`**, uma só, com as chaves do SDK (`MaxXlsxRows`, `MaxPdfRows`,
  `MaxConcurrentExports`, `TempDirectory`) e o limite por pessoa (`PorPessoaPorMinuto`). O SDK faz bind
  simples da seção e ignora a chave a mais. A issue propunha `Intranet:Documents`, que ficaria colada em
  `Intranet:Documentos` (armazenamento e chave dos documentos do setor): nas variáveis de ambiente,
  `Intranet__Documents__*` ao lado de `Intranet__Documentos__*`.

### 4. Rate limiting

- Política nomeada `"exportacao"`, janela fixa de 1 minuto, partição pelo `sub`, `QueueLimit = 0`,
  `429` ao passar. Limite configurável, padrão **10 por minuto**, valor menor que 1 derruba a subida.
  Mesmo desenho do `ExportRateLimiting` do LogStream.
- O contador é da instância: com N réplicas, o teto efetivo é N vezes o configurado. Perda do contador
  é tolerável.
- `UseRateLimiter()` entra no pipeline **depois** da autenticação e da resolução de tenant, para a
  partição enxergar o `sub`. Sem `sub` (modo aberto de DEV), todos caem numa partição só.
- O `429` sai pela página de status padrão do host (`UseStatusCodePages`). Uma tela amigável para o
  limite fica com o parcial de tema, fora desta entrega.
- A política só existe para quem a aplica pelo nome. Nenhuma rota do core a usa nesta entrega.

### 5. Como uma RCL exporta (o que o `extensao.md` passa a ensinar)

- **Formulário `POST`** com antiforgery (`<form method="post">`, que o tag helper já protege) e os
  filtros da tela em campos ocultos. Nunca `GET` (ADR-0058). Sem script inline, então passa na CSP.
- **Controller:** `[HttpPost]`, `[ValidateAntiForgeryToken]`, `[EnableRateLimiting(<constante>)]` e a
  **política de permissão da própria RCL** (o equivalente a `<recurso>:export` da ADR-0058). Esconder o
  botão não basta.
- **Ordem:** gerar → `ITrilhaDeExportacao` → enviar com `ToHttpResult()`. Se a trilha recusar, descartar
  o documento (`DisposeAsync`) e voltar à tela com a mensagem genérica "não foi possível exportar agora"
  pelo `TempData`, como os demais retornos. Exceção depois de gerar também descarta o documento.
- `TooManyRows` volta à tela pedindo para restringir o filtro.
- Exemplo de código completo no guia, no formato da exportação do LogStream.

## Testes

- **Unidade:** a escolha do adapter pelos três casos da tabela, com `IHostEnvironment` falso
  (`Development`, `Testing`, `Staging`/`Production`). O adapter do LogStream devolve falha para recusa,
  rede e timeout. O corte de 200 caracteres e a regra da chave do recurso. A trilha atual continua
  engolindo (os testes existentes não mudam).
- **Integração** (`IntranetWebFactory`, ambiente `Testing`): um controller de teste no assembly de
  testes, que já entra como application part externa, como os `IItemDeMenuPrincipal` de teste.
  - Em `Testing` sem LogStream, a exportação passa: `200`, `Content-Disposition: attachment`.
  - Com uma trilha falsa que falha, injetada pela fábrica (no mesmo padrão das propriedades
    `GestaoDeAcesso`/`ArquivoStore`), a exportação é recusada e nenhum arquivo sai. O falso substitui
    a porta obrigatória da Application, e não a `ITrilhaDeExportacao`, para o teste passar pela
    implementação real do contrato.
  - `POST` sem token antiforgery é recusado.
  - Acima do limite, `429`, com o limite baixado pela configuração da fábrica.
- Fora de DEV sem LogStream fica coberto pelo teste de unidade da escolha. Subir o host de integração em
  `Production` exigiria desligar outras guardas de subida, o que não vale o custo.

## Documentação

- `docs/extensao.md`: seção nova sobre exportação, com o passo a passo da seção 5.
- `docs/guia-de-deploy.md`: as chaves da seção de exportação e o limite por pessoa. Também o aviso de que,
  fora de DEV, exportar **exige** `Intranet:Auditoria:LogStreamUrl`, ao contrário do resto da auditoria.
- **ADR-0013** em `docs/adr/secco-intranet-adrs.md`: a trilha fail-closed ao lado da fail-open, com
  critérios diferentes de propósito, e a ampliação do `Extensibility` para além do menu (ADR-0011).
- `docs/roadmap.md`: o item da issue #5.

## Fica com quem implementa

- Nomes de tipos, métodos e arquivos, desde que sigam o idioma e as convenções do repositório.
- Se a porta obrigatória da Application compartilha código com o adapter atual.
- O formato da mensagem de log, sem dado sensível (ADR-0020).
