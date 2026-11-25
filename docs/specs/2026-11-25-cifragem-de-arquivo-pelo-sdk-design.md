# Cifragem de arquivo pelo SDK — design

**Estado:** proposta.

## Por quê

A intranet cifra todo arquivo enviado (documentos, imagens do Mural, fotos do Diretório) com um cifrador próprio, o `EnvelopeCipher` ([ADR-0005](../adr/secco-intranet-adrs.md)). É código criptográfico delicado, e outro produto que guardasse arquivo teria que escrever o seu. Por isso a capacidade foi pedida à plataforma ([secco-platform#54](https://github.com/rafsecco/secco-platform/issues/54)), que a entregou em `Secco.SDK.EntityFrameworkCore` **0.7.0** (ADR-0061 da plataforma): `ISeccoFileCipher` / `AesGcmFileCipher`.

Esta mudança troca o cifrador próprio pelo do SDK e apaga o `EnvelopeCipher`.

## Objetivos

- Todo arquivo novo é cifrado e decifrado pelo `ISeccoFileCipher` do SDK.
- O `EnvelopeCipher` e seus testes saem do repositório.
- A política da chave mestra continua sendo do produto, sem mudar nada para quem instala. Valem as mesmas regras de hoje:
  - a seção `Intranet:Documentos:Chave` (`ChaveAtiva`, `ChavesAposentadas`);
  - a chave de desenvolvimento aceita só em Development;
  - a aplicação não sobe sem chave válida fora de Development.
- A documentação diz a verdade sobre a troca da chave mestra, que hoje não tem ferramenta.

## Fora do escopo

- **Rotina de troca da chave mestra.** É o reembrulho das chaves dos arquivos em todos os tenants, com `IsKeyWrappedWithActiveKey` e `RewrapKey`. Vira item no roadmap.
- Ferramenta para recifrar arquivos já gravados no formato antigo.
- KMS, ou mudar a origem da chave mestra.
- Mudar o nome da seção de configuração ou das variáveis de ambiente.

## O que não pode acontecer

- A regra da chave de desenvolvimento passar para o SDK, ou a aplicação subir fora de Development com a chave embutida ou sem chave.
- Sobrar arquivo parcial no disco depois de uma gravação que falhou, inclusive por tamanho acima do limite.
- Um download adulterado ou cortado ser entregue como completo. O comportamento continua o de hoje: a resposta é abortada.
- A documentação continuar prometendo uma troca de chave que regrava as chaves dos arquivos.

## Abordagens

- **A. Só trocar o cifrador (escolhida).** É uma mudança pequena e mecânica: o cifrador é usado num ponto só, o `SistemaArquivosArquivoStore`. A rotina de troca de chave vai para o roadmap, e a doc é corrigida agora.
- **B. Trocar e já criar a rotina de troca de chave.** É um comando de operação que percorre o banco de cada tenant e as quatro colunas de chave. Fica umas três vezes maior e tem decisões de operação próprias. Ficou para depois.
- **Recifrar os arquivos antigos em vez de descartá-los.** Descartado: a primeira instalação ainda está em testes, e quebrar dado existente é aceito, desde que fique registrado.

## Como fica

**Pacotes.** `Secco.SDK.EntityFrameworkCore` sobe de 0.6.0 para 0.7.0. `Secco.SharedKernel` sobe para a estável mais recente (hoje 0.7.14). As demais versões `Secco.*` são conferidas no feed na hora de implementar.

**Chave mestra.**

- `ChaveMestraOptions` fica como está.
- Uma fábrica do produto lê as opções e o `IHostEnvironment` e monta `AesGcmSecretCipher.FromBase64Keys(ativa, aposentadas)`. Ela aplica as mesmas regras de hoje:
  - ausência fora de Development falha;
  - a chave tem de estar em base64 válido e ter 32 bytes;
  - os erros de configuração continuam lançando `ChaveMestraException`.
- O `ISeccoFileCipher` é registrado como singleton sobre esse cifrador.
- `ValidacaoChaveMestraHostedService` passa a resolver o `ISeccoFileCipher`. A guarda de subida continua igual, e os `StartupChaveMestraTests` continuam passando.

**Armazenamento.** O `SistemaArquivosArquivoStore` passa a depender de `ISeccoFileCipher`.

- **Gravar:** `EncryptAsync(conteudo, destino, maxPlaintextLength: TamanhoMaximoBytes)`. A chave embrulhada e o tamanho vêm do resultado, e o produto não toca mais em chave de arquivo nenhuma. O `catch` que apaga o arquivo parcial continua cobrindo qualquer exceção.
- **Ler:** `DecryptAsync(origem, destino, chaveEmbrulhada)`.
- `IArquivoStore`, `ArquivoGravado`, handlers, colunas e telas não mudam.

**Exceções.**

- **Tamanho acima do limite:** `SeccoFileTooLargeException`, no lugar da `ChaveMestraException`. É só uma rede de segurança, porque os handlers já recusam o tamanho antes de gravar.
- **Arquivo adulterado, cortado ou com chave desconhecida:** `SeccoSecretCipherException`.
- Nenhum chamador captura `ChaveMestraException` hoje, então o tratamento não muda.

**Formato.** Passa a ser o `SECCOENC` v1 do SDK, com blocos de 64 KiB e a construção STREAM. A chave embrulhada continua no prefixo `secco-enc:v1:`, mas embrulha outro valor (o base64 da chave). Quebra: um arquivo gravado antes desta mudança não abre mais. O jeito de limpar é apagar os arquivos e os registros que apontam para eles, ou zerar o banco e a pasta de armazenamento de dev.

Esta é a última troca de formato sem custo. Com uma instalação em uso, mudar o formato de novo exige recifrar: ler, decifrar, cifrar e regravar cada arquivo de cada tenant. O custo cresce com o volume guardado, e a operação exige espaço livre para a cópia, troca atômica junto com a chave no banco e retomada depois de uma queda. Já trocar a chave mestra só reembrulha 32 bytes por arquivo no banco e não toca o disco.

**Testes.**

- Saem os `EnvelopeCipherTests`.
- Os `SistemaArquivosArquivoStoreTests` passam a montar o cifrador do SDK e cobrem:
  - ida e volta;
  - arquivo gravado começando com `SECCOENC`, o que prova que o SDK está em uso;
  - recusa de arquivo adulterado e de chave de outra chave mestra;
  - limite de tamanho apagando o parcial.
- A política da chave ganha testes de unidade:
  - chave ausente em Development usa a embutida;
  - chave ausente fora de Development falha;
  - base64 inválido falha;
  - tamanho errado falha;
  - aposentada decifra.
- Os testes de integração de documento continuam passando sem mudança.

**Documentação.**

- `docs/seguranca.md`:
  - blocos de 1 MiB passam a 64 KiB, e o texto cita o SDK;
  - "Troca de chave" passa a dizer o que existe de fato: uma chave nova cifra os arquivos novos, as aposentadas seguem necessárias para ler os antigos, e ainda não há ferramenta que converta as chaves antigas. Por isso uma chave aposentada não pode ser descartada.
- `docs/guia-de-deploy.md`: a variável `ChavesAposentadas` deixa de mencionar um reembrulho que não existe.
- `ChaveMestraOptions`: o comentário de `ChavesAposentadas` deixa de mencionar esse reembrulho.
- ADR-0005: nota datada de que a cifragem vem do SDK, com formato `SECCOENC` e blocos de 64 KiB. A decisão em si (envelope, chave por arquivo, porta, caminho por tenant) não muda.
- `docs/plataforma.md`: a #54 passa de "Demandas abertas" para "Atendidas", com versão e data.
- `docs/roadmap.md`:
  - o item "Avaliar trocar o embrulho de chave do `EnvelopeCipher`" fecha;
  - entra o item da rotina de troca da chave mestra, que reembrulha em todos os tenants e permite descartar a aposentada.
- `CHANGELOG.md` (Não lançado): a troca, a subida de pacote e a quebra de formato.

## Fica com quem implementa

- O nome e o lugar da fábrica da chave mestra (classe estática ou método na extensão de DI).
- Se `ChaveMestraException` continua com esse nome.
- A redação final das notas na ADR-0005 e no CHANGELOG.
