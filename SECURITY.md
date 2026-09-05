# Política de segurança

A Secco.Intranet guarda documentos internos de uma instituição, decide quem enxerga cada um e
opera sobre bancos separados por tenant. Uma falha aqui expõe documento de setor a quem não
deveria vê-lo, ou dado de um cliente a outro. Segurança é critério de design, não revisão
posterior (ADR-0020 da plataforma).

## Reportar uma vulnerabilidade

**Não abra issue pública** para vulnerabilidade. Use o canal privado do GitHub:

> Repositório → aba **Security** → **Report a vulnerability**

Isso abre um advisory privado, visível apenas para os mantenedores, onde a correção pode ser
preparada antes de qualquer divulgação.

No relato, ajuda muito ter:

- versão ou commit afetado;
- o que um atacante consegue fazer, não só o que está errado;
- passos de reprodução, se houver;
- o limite do impacto, se você já souber — um usuário, um setor, um tenant, todos.

## Versões suportadas

O produto ainda não atingiu 1.0. **Só a versão mais recente recebe correção**; não há backport.

## Escopo

Interessa qualquer coisa que quebre uma destas garantias, que são as que este produto promete:

- **Isolamento de tenant** ([ADR-0005 da plataforma](https://github.com/rafsecco/secco-platform))
  — dado ou arquivo de um tenant alcançável a partir de outro, por qualquer caminho. O caminho
  do arquivo é montado dentro do `IArquivoStore` a partir do tenant corrente, nunca pelo
  chamador; furar isso é vulnerabilidade.
- **Visibilidade de documento** ([ADR-0005](docs/adr/secco-intranet-adrs.md)) — ler documento
  marcado como `Setor` sem ter a Role daquele setor, ou alcançar o conteúdo por fora do
  endpoint de download. Arquivos **nunca** são servidos como conteúdo estático nem têm endereço
  adivinhável, justamente porque a regra de visibilidade precisa ser avaliada a cada leitura.
- **Cifragem em repouso** ([ADR-0005](docs/adr/secco-intranet-adrs.md)) — recuperar conteúdo de
  documento a partir do armazenamento sem a chave mestra, ou fazer o decifrador aceitar arquivo
  adulterado, reordenado ou truncado.
- **Autorização por setor** ([ADR-0001](docs/adr/secco-intranet-adrs.md)) — publicar, editar ou
  arquivar em setor que o usuário não administra.
- **Vazamento de informação** — stack trace, segredo, caminho interno ou dado de outro tenant em
  resposta ou log. Inclui distinguir "não existe" de "existe mas você não pode ver": os dois
  devolvem o mesmo erro de propósito, e uma resposta que os separe é vulnerabilidade, não
  detalhe de usabilidade.

Fora de escopo: as credenciais de desenvolvimento versionadas no repositório. Elas são
conhecidas de propósito — o `sa` do `docker-compose.yml` e a chave mestra de desenvolvimento
embutida em `ChaveMestraOptions`. A guarda é o ambiente: em `Production`, a aplicação **falha o
startup** se a chave mestra não for configurada.

## Dependências

O repositório usa Central Package Management: toda versão vive em `Directory.Packages.props`.
Pins para escapar de CVE **envelhecem** — a verificação é manutenção contínua, não decisão de
uma vez só:

```bash
dotnet list Secco.Intranet.slnx package --vulnerable --include-transitive
```

O build trata `NU1903` como sinal a investigar, não ruído.
