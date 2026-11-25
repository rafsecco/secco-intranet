# Troca da chave mestra — design

**Estado:** proposta.

## Por quê

A chave de cada arquivo é embrulhada pela chave mestra (`Intranet:Documentos:Chave`) e guardada no banco. Configurar uma chave nova já funciona: ela cifra os arquivos novos, e as antigas, em `ChavesAposentadas`, continuam lendo os anteriores. Falta converter as chaves dos arquivos antigos para a chave nova. Sem isso, uma chave aposentada nunca pode ser descartada, e trocar a chave não serve para o caso que importa: tirar de uso uma chave que vazou ou que precisa sair de circulação.

O SDK já entrega as peças (`ISeccoFileCipher.IsKeyWrappedWithActiveKey` e `RewrapKey`, ADR-0061 da plataforma). Esta mudança é a rotina que as usa, deixada como item no roadmap pela [cifragem de arquivo pelo SDK](2026-11-25-cifragem-de-arquivo-pelo-sdk-design.md).

## Objetivos

- A cada `migrate`, toda chave de arquivo embrulhada por uma chave aposentada é reembrulhada pela ativa, em todos os tenants. Isso vale para as quatro colunas:
  - `ds_chave_embrulhada` do documento;
  - `ds_imagem_chave_embrulhada` da publicação;
  - `ds_chave_embrulhada` da imagem no texto;
  - `ds_foto_chave_embrulhada` do perfil.
- Nenhum arquivo é lido nem regravado, só a coluna da chave.
- Quem opera tem um sinal claro de quando a chave aposentada pode sair da configuração.
- A documentação passa a descrever um procedimento de troca que existe.

## Fora do escopo

- Recifrar o conteúdo dos arquivos, que só seria necessário se a chave do próprio arquivo vazasse.
- KMS, ou mudar a origem da chave mestra.
- Tela ou comando separado para a troca.
- Apagar chaves aposentadas da configuração automaticamente.

## O que não pode acontecer

- **Perder arquivo ao reembrulhar:** a chave gravada tem de abrir o mesmo arquivo que a anterior abria.
- **Sobrescrever uma troca de arquivo:** se o usuário trocar o arquivo enquanto a rotina roda, a chave nova do arquivo novo não pode ser sobrescrita pela chave reembrulhada do antigo.
- **Expor chave ou caminho:** log e erro nunca trazem chave, embrulhada ou não, nem caminho de arquivo.
- **Falha silenciosa:** uma chave que nenhuma chave configurada abre não pode passar sem aviso. Ela indica uma aposentada removida cedo demais, e o arquivo já está ilegível.
- **Parar os outros tenants:** um tenant com problema não impede os demais de convergir.

## Abordagens

- **A. Dentro do `migrate` (escolhida).** Trocar a chave vira mudar a configuração e fazer um deploy normal. Quando não há nada a converter, a passada não muda nada e roda em todo deploy. É o mesmo desenho da plataforma no SecureGate, que converte as connection strings do catálogo para a chave ativa no `migrate` (`TenantDatabaseReEncryptionSeeder`).
- **B. Verbo próprio (`reembrulhar-chaves`).** Dá controle explícito do momento, mas é mais um comando no guia de deploy, que precisa ser lembrado. Descartada.
- **C. Tarefa em segundo plano na subida da aplicação.** Descartada: fora de Development, nada de banco acontece como efeito colateral da subida (ADR-0038 da plataforma).

## Como fica

**Onde roda.** Dentro do `IntranetTenantMigrator`, logo depois de aplicar as migrations de cada tenant e com o mesmo contexto. Só roda num tenant cujas migrations passaram, para nunca agir sobre um schema pela metade. A lógica fica numa classe própria, que o migrator chama por tenant, e não dentro do laço de migrations.

**O que faz, por coluna:**

- lê só o id e a chave das linhas com chave preenchida (não nula nem vazia), sem rastreamento;
- pula as chaves que `IsKeyWrappedWithActiveKey` dá como embrulhadas pela ativa;
- para as demais, grava `RewrapKey(chave)` com uma atualização condicionada: `WHERE id = @id AND chave = @chaveLida` (`ExecuteUpdateAsync`, sem SQL cru, nos dois provedores);
- se a linha mudou entre a leitura e a escrita, a atualização não acontece e a linha fica para o próximo `migrate`;
- não toca nenhuma outra coluna, nem data de alteração.

**Chave que nenhuma chave configurada abre.** `RewrapKey` lança `SeccoSecretCipherException`. A linha é contada como "sem chave conhecida" e a passada continua nas outras linhas e colunas. Ao fim, o tenant entra na lista de falhas do migrator: o `migrate` sai com código 1, e o seed de referência não roda. É a mesma regra de hoje para qualquer falha de tenant. Uma aposentada removida cedo demais vira um deploy que falha alto, não arquivo perdido em silêncio.

**Log, por tenant, quando houve algo:** id do tenant, quantas chaves foram reembrulhadas e quantas ficaram sem chave conhecida, por tabela. Nunca a chave nem o caminho. Um tenant sem nada a fazer não gera log. Quem opera procura duas coisas: "reembrulhadas" diferente de zero numa passada e "sem chave conhecida".

**Procedimento de troca** (guia de deploy):

1. Gere a chave nova. Ponha a atual em `ChavesAposentadas` e a nova em `ChaveAtiva`.
2. Faça o deploy normal. O `migrate` reembrulha o que encontrar.
3. Durante o deploy, o container antigo ainda sobe arquivos com a chave antiga. Depois que todos os containers estiverem na configuração nova, rode o `migrate` de novo.
4. Quando um `migrate` terminar com código 0 e sem nenhuma chave reembrulhada em nenhum tenant, a aposentada pode sair da configuração.
5. Guarde a chave retirada no cofre enquanto existir backup que dependa dela. Isso já está no guia.

**Arquivos no formato anterior ao SDK.** Os que sobraram de antes da troca de cifrador (descartados pela spec anterior) têm chave embrulhada pela mesma chave mestra. `IsKeyWrappedWithActiveKey` os dá como em dia, e a rotina não os toca. Numa troca de chave, `RewrapKey` pode recusá-los e eles caem em "sem chave conhecida". O efeito é o esperado: esses arquivos já não abrem. A primeira instalação ainda está em testes, então não há tratamento especial.

**Testes.**

- **Integração**, com o banco de verdade e dois tenants da `IntranetWebFactory`:
  - registros nas quatro tabelas, com chave embrulhada pela chave A;
  - host com ativa B e aposentada A, rodando `RunSeccoMigrationsAsync`;
  - todas as chaves passam a estar embrulhadas por B, nos dois tenants;
  - cada arquivo ainda é lido pelo `IArquivoStore` com o mesmo conteúdo, inclusive depois de tirar A da configuração;
  - uma segunda passada não muda nenhuma linha.
- **Sem chave conhecida:** uma linha embrulhada por uma chave C, que não está configurada, faz o `migrate` devolver falha para aquele tenant. As outras linhas do tenant são reembrulhadas mesmo assim, e o outro tenant converge.
- **Corrida:** a atualização condicionada não sobrescreve uma linha cuja chave mudou depois da leitura. Pode ser testada direto na classe da rotina, trocando a chave entre a leitura e a escrita.
- **Log:** a mensagem não contém nenhuma chave.

**Documentação.**

- `docs/seguranca.md`, "Troca de chave": a troca passa a existir. Descrever o procedimento em uma frase e citar o guia de deploy.
- `docs/guia-de-deploy.md`:
  - nova seção "Troca da chave mestra", com o procedimento acima;
  - a linha de `ChavesAposentadas` e as notas de rotação e de restauração deixam de dizer que não há ferramenta.
- `ChaveMestraOptions`: os comentários deixam de dizer que não há rotina.
- ADR-0005: a nota "A rotina que faz isso ainda não existe" passa a dizer que o `migrate` faz o reembrulho.
- `docs/roadmap.md`: o item "Rotina de troca da chave mestra" é marcado `[x]`, com a data.
- `CHANGELOG.md` (Não lançado): a troca de chave pelo `migrate`.

## Fica com quem implementa

- O nome e o lugar da classe da rotina, e como o migrator a recebe. O migrator cria o contexto por tenant, então ela recebe o contexto, ou o migrator recebe uma fábrica.
- Se as quatro colunas são percorridas por um laço genérico ou por quatro chamadas explícitas.
- A redação exata do log e das notas de documentação.
