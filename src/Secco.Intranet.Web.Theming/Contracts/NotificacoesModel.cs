namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Uma linha do sino.</summary>
/// <param name="Id">Identificador da notificação.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Mensagem">Resumo.</param>
/// <param name="Link">Destino do clique; nulo desabilita a navegação.</param>
/// <param name="Quando">Texto relativo já formatado, como "há 2 h".</param>
public sealed record NotificacaoNoSinoModel(
	Guid Id,
	string Titulo,
	string Mensagem,
	string? Link,
	string Quando);

/// <summary>
/// Sino de notificações. O Hub só expõe não lidas, então a lista é exatamente isso — não há
/// histórico de lidas a mostrar, nem "marcar todas", que seriam N chamadas.
/// </summary>
/// <param name="NaoLidas">Quantidade não lida; zero esconde o contador.</param>
/// <param name="Itens">Notificações não lidas, da mais recente para a mais antiga.</param>
/// <param name="Habilitado">
/// Quando <c>false</c>, o tema não renderiza o sino — é o caso do modo aberto de DEV, sem
/// usuário identificado.
/// </param>
public sealed record NotificacoesModel(
	int NaoLidas,
	IReadOnlyList<NotificacaoNoSinoModel> Itens,
	bool Habilitado);
