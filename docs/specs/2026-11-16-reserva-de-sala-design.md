# Reserva de sala — design

**Estado:** aprovado em 2026-11-16; aguardando implementação.

Segundo dos três projetos que saíram do item "Ferramentas do dia a dia" do [roadmap](../roadmap.md). O primeiro, [Links do setor](2026-11-15-links-do-setor-design.md), está entregue; o terceiro, o motor de processos, tem conversa e spec próprias.

## Por quê

As salas de reunião são reservadas hoje direto na agenda de e-mail, e isso gera confusão: cada pessoa reserva de um jeito e não há um lugar único que diga quem está com a sala. A intranet passa a ser esse lugar. Ela guarda as reservas, impede dois usos da mesma sala no mesmo horário e, quando a instalação quiser, espelha cada reserva na agenda do Microsoft 365.

## Objetivos

- O `intranet-admin` cadastra as salas.
- Qualquer pessoa logada vê a ocupação do dia e reserva um horário livre, com convidados do Diretório.
- A intranet decide o conflito de horário. Ela funciona sozinha, sem depender de serviço externo.
- Espelhamento opcional na agenda, por instalação. O primeiro provedor é o Microsoft 365; o desenho aceita outros depois.

## Fora do escopo

- Reserva que se repete (diária, semanal). Etapa seguinte prevista: repetição simples com data final.
- Regras de horário (expediente, duração máxima, antecedência máxima).
- Convidado de fora do Diretório.
- Google e Apple como provedores de agenda.
- Trazer para a intranet o que acontece na agenda: respostas dos convidados, alterações feitas lá.
- Link de reunião online.
- Aviso por e-mail ao convidado quando o espelhamento está desligado.
- Aprovação de reserva e salas restritas a um setor.

## O que não pode acontecer

- Duas reservas da mesma sala com horários sobrepostos, mesmo quando duas pessoas gravam no mesmo instante.
- Uma reserva deixar de ser gravada, ou ficar inválida, porque a agenda externa está fora do ar ou mal configurada.
- Alguém que não é o dono da reserva nem `intranet-admin` alterá-la ou cancelá-la.
- A intranet precisar de permissão sobre a agenda de todas as pessoas da organização.

## Como fica para quem usa

**Menu.** Item fixo "Salas" na raiz, depois do Diretório, para qualquer pessoa logada. Entra no catálogo de itens do menu principal, como Mural e Diretório.

**Grade do dia** (`/salas`). Uma coluna por sala ativa, as horas na vertical, cada reserva como um bloco com o título e o nome de quem reservou. A pessoa troca o dia por setas e por um campo de data. No celular aparece uma sala por vez, com troca por abas. Clicar num espaço livre abre o formulário já com a sala e o horário preenchidos.

**Formulário de reserva.** Sala, data, início, fim, título, descrição (opcional) e convidados, escolhidos por nome entre as pessoas do Diretório. Sem JavaScript o formulário continua funcionando, com os campos de hora digitados.

**Detalhe da reserva.** Qualquer pessoa logada abre o bloco e vê título, horário, quem reservou e os convidados. Quem reservou e o `intranet-admin` veem também Alterar e Cancelar.

**Minhas reservas** (`/salas/minhas`). As reservas futuras que a pessoa fez ou para as quais foi convidada, em ordem de data.

**Cadastro de salas** (`/salas/gerenciar`, só `intranet-admin`). Lista, criar, editar, desativar, excluir.

## Regras

| Assunto | Regra |
|---|---|
| Quem reserva | Qualquer pessoa logada |
| Quem altera ou cancela | Quem reservou e o `intranet-admin`. Para os demais a ação responde 404, como no resto do produto |
| Conflito | Mesma sala e intervalos sobrepostos: recusado, com mensagem dizendo quem está com a sala. Uma reserva pode começar no minuto em que outra termina |
| Horário | Início e fim no mesmo dia, fim depois do início, em passos de 15 minutos |
| Passado | Não se cria reserva que já começou; reserva que já começou não é alterada nem cancelada |
| Fuso | Um fuso por instalação (`Intranet:Salas:FusoHorario`, padrão `America/Sao_Paulo`, como nos Vencimentos). O banco guarda em UTC |
| Título | Obrigatório, até 120 caracteres. Descrição até 1000 |
| Convidados | Pessoas do Diretório, sem repetir e sem incluir quem reservou. Limite de 50 |
| Sala inativa | Some da grade e do formulário. As reservas já feitas continuam valendo e aparecendo em "Minhas reservas" |
| Excluir sala | Só sem reserva futura. Com reserva futura, a tela orienta a desativar |
| Cancelar | Apaga a reserva. Não fica histórico de canceladas |

**Sala.** Nome (obrigatório, único), localização, capacidade, recursos (texto livre, como "TV, videoconferência"), ativa, ordem na grade e e-mail da sala na agenda externa (opcional).

## Conflito garantido no banco

A checagem "existe reserva sobreposta?" seguida da gravação não basta: duas requisições simultâneas passam as duas. A gravação acontece numa transação que primeiro trava a linha da sala (`SELECT … FOR UPDATE` no PostgreSQL, `WITH (UPDLOCK, ROWLOCK)` no SQL Server) e só então confere a sobreposição e grava. Assim as reservas de uma mesma sala entram em fila, e as de salas diferentes não se atrapalham. A alteração de horário ou de sala usa o mesmo caminho, travando a sala de destino.

É o primeiro uso de SQL escrito à mão no produto. Fica isolado numa única peça da Infrastructure, com uma variante por banco, sem dado do usuário concatenado.

## Espelhamento na agenda

### Abordagens

1. **Caixa de correio de serviço como organizadora (escolhida).** A intranet cria o evento numa caixa própria (por exemplo "Reserva de Salas") e convida quem reservou, os convidados e a sala.
2. **Quem reservou como organizador.** O convite chega no nome da pessoa, mas a intranet precisa de escrita na agenda de todos, a pessoa pode mudar o horário pela agenda sem a intranet saber, e a sala não distingue esse pedido de uma reserva feita direto na agenda.
3. **A agenda como fonte da verdade,** com a intranet só consultando disponibilidade. É o que gera a confusão de hoje, e deixa a intranet inútil sem a agenda.

A escolhida fecha a reserva direta de verdade (a sala passa a aceitar pedidos só da caixa de serviço), pede permissão sobre uma única caixa e mantém a intranet como o único lugar onde a reserva muda. O custo: o convite chega como enviado pela caixa de serviço, com o nome de quem reservou no texto.

### Comportamento

- **Desligado por padrão.** Sem configuração, nada sai da intranet e nenhum estado de espelhamento aparece nas telas.
- **A reserva é gravada primeiro.** A resposta à pessoa não espera a agenda.
- **Um processo em segundo plano** envia as pendências: cria o evento, atualiza quando a reserva muda e apaga quando ela é cancelada. A agenda externa se encarrega de mandar convites, atualizações e cancelamentos.
- **Evento.** Assunto: o título da reserva. Corpo: "Reservado por {nome}" e a descrição. Local: nome da sala. Participantes: quem reservou e os convidados que têm e-mail no Diretório; a sala entra como recurso quando tem e-mail cadastrado.
- **Estado por reserva:** aguardando, espelhada ou falhou. Aparece no detalhe para quem reservou e para o `intranet-admin`.
- **Falha.** Erro passageiro (rede, limite de chamadas, serviço fora) é tentado de novo com espera crescente. Depois de um número de tentativas, ou em erro definitivo (permissão negada, caixa inexistente), a reserva fica em "falhou" com o motivo, e o `intranet-admin` tem um botão para tentar de novo. A reserva continua valendo na intranet.
- **Cancelamento.** Como a reserva some da tabela ao ser cancelada, a exclusão do evento fica numa fila própria de pendências, que guarda só o identificador do evento.
- **Mão única.** Nada da agenda volta para a intranet.
- **Vários tenants.** O processo percorre os tenants do catálogo, um escopo por tenant, como o de Vencimentos.

### Configuração

| Chave | Valor |
|---|---|
| `Intranet:Agenda:Provedor` | Vazio (padrão, sem espelhamento) ou `Microsoft`. Valor desconhecido derruba a subida |
| `Intranet:Agenda:Microsoft:TenantId` | Identificador (GUID) da organização |
| `Intranet:Agenda:Microsoft:ClientId` | Identificador (GUID) do aplicativo registrado |
| `Intranet:Agenda:Microsoft:ClientSecret` | Segredo do aplicativo. Só por variável de ambiente ou cofre, nunca em arquivo versionado |
| `Intranet:Agenda:Microsoft:CaixaOrganizadora` | E-mail da caixa de serviço |

Com `Provedor=Microsoft`, qualquer uma das quatro faltando ou malformada derruba a subida, como na configuração do chat.

### Microsoft 365

Conferido na documentação da Microsoft em 2026-11-16:

- Criar, alterar e apagar evento na agenda de um usuário sem login pede a permissão de aplicativo `Calendars.ReadWrite`; não há opção menor.
- Evento criado com participantes dispara os convites sozinho; apagar o evento na agenda do organizador manda o cancelamento.
- A sala entra como participante do tipo `resource`, com o e-mail dela.
- Para limitar o aplicativo a caixas específicas, o caminho atual é o "RBAC for Applications" do Exchange Online, que substitui as Application Access Policies. O papel é `Application Calendars.ReadWrite`, com escopo na caixa de serviço. A permissão ampla no Entra ID não deve ser concedida, porque as duas se somam.

O guia de deploy ganha o passo a passo do administrador do Microsoft 365: registrar o aplicativo, criar a caixa de serviço, conceder o papel com escopo nela, e configurar cada sala para aceitar pedidos só dessa caixa. Os comandos exatos são conferidos na documentação ao escrever o guia.

## Como é construído

- **Domain:** `Sala` e `Reserva` (com os convidados). As regras de horário e de quem pode alterar ficam nas entidades e nos handlers, com Result pattern.
- **Application:** handlers de salas e de reservas; a interface `IAgendaExterna` (criar, atualizar e apagar evento), que o espelhamento usa sem saber qual é o provedor.
- **Infrastructure:** tabelas novas com migration pareada (SQL Server e PostgreSQL) e nomes pela convenção do ADR-0017; a gravação com trava da sala; o processo de espelhamento; a implementação do Microsoft atrás de `IAgendaExterna`. A versão das bibliotecas do Microsoft é a estável mais recente, conferida ao vivo.
- **Web:** `SalasController`, as views no core, e o item no catálogo do menu principal. A grade usa só classes e CSS dos temas, sem estilo ou script embutido (CSP em modo Enforce).
- **Plataforma:** a conexão com a agenda do Microsoft pode interessar a outro produto da Secco Platform. Hoje só a intranet usa, então ela nasce aqui, atrás da interface, pronta para ser movida.

## Quebras a registrar no CHANGELOG

- Migration nova (salas, reservas, convidados e pendências de espelhamento).
- Tema próprio (fork): classes novas para a grade de salas no CSS do tema. Tema que não as tenha mostra a grade sem o desenho.
- Item "Salas" novo no menu principal de toda instalação.

## Testes

- **Unidade:** regras de horário (mesmo dia, passos de 15 minutos, passado), sobreposição (inclusive encostada), quem pode alterar, convidados, montagem do evento, política de novas tentativas, configuração inválida derrubando a subida.
- **Integração, nos dois bancos:** duas gravações simultâneas na mesma sala, só uma entra; salas diferentes não se bloqueiam; telas e permissões (404 para quem não é dono); sala inativa e exclusão com reserva futura; espelhamento com uma agenda falsa (criar, alterar, cancelar, falha passageira e definitiva).
- **Tela (opt-in):** a grade nos dois temas, no desktop e no celular; reservar clicando num espaço livre; sem violação de CSP.
- A chamada real ao Microsoft não entra na suíte automática. Fica um roteiro de conferência manual no guia de deploy.
