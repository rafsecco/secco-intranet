namespace Secco.Intranet.Application.Auditoria;

/// <summary>Quem está executando a ação.</summary>
/// <param name="Id">Identificador do usuário (claim <c>sub</c>).</param>
/// <param name="Nome">Nome de exibição.</param>
public sealed record AtorDaAcao(string Id, string Nome);

/// <summary>
/// Porta de resolução do usuário atual. Existe porque o <c>SeccoAmbientContext</c> carrega
/// tenant e correlação, mas não usuário — e passar o ator em cada comando repetiria oito vezes
/// o que a notificação precisou fazer uma vez.
/// </summary>
public interface IAtorAtual
{
	/// <summary>
	/// Ator da requisição atual, ou <c>null</c> quando não há usuário identificado — o modo
	/// aberto de DEV. Sem ator, nada é registrado: entrada com ator inventado não prova nada.
	/// </summary>
	AtorDaAcao? Atual();
}
