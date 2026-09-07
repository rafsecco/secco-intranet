namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>Um destino do lote.</summary>
/// <param name="UsuarioId">Identificador do usuário, para o inbox in-app.</param>
/// <param name="Email">E-mail já validado, ou nulo quando o lote não pede o canal de e-mail.</param>
public sealed record DestinoDaMensagem(Guid UsuarioId, string? Email);

/// <summary>Um conteúdo endereçado a muitos destinos.</summary>
/// <param name="Titulo">Título da notificação.</param>
/// <param name="Resumo">Corpo em texto puro, já truncado.</param>
/// <param name="Link">Destino do clique.</param>
/// <param name="Canais">Canais pedidos: <c>in_app</c>, <c>email</c>, <c>teams</c>, <c>slack</c>.</param>
/// <param name="Origem">Vai no campo <c>Source</c> do Hub.</param>
/// <param name="Tipo">Vai no campo <c>Type</c> do Hub.</param>
/// <param name="Destinos">Destinos deste lote, no máximo 500.</param>
/// <param name="ProgramadaPara">
/// Instante da entrega; nulo entrega agora. O Hub segura e-mail <b>e</b> item do sino até a
/// hora — sem isso, o aviso apareceria no sino avisando sobre algo que o mural ainda não
/// mostra.
/// </param>
public sealed record MensagemParaEnviar(
	string Titulo,
	string Resumo,
	string Link,
	IReadOnlyList<string> Canais,
	string Origem,
	string Tipo,
	IReadOnlyList<DestinoDaMensagem> Destinos,
	DateTimeOffset? ProgramadaPara);

/// <summary>
/// Porta de envio. Fila, retry e entrega são do Hub (ADR-0006) — daqui sai uma chamada por
/// lote e nada mais.
/// </summary>
public interface INotificadorDeMensagens
{
	/// <summary>Envia um lote.</summary>
	/// <param name="mensagem">Conteúdo e destinos.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <exception cref="NotificacaoIndisponivelException">Quando o Hub não aceita o lote.</exception>
	Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default);
}

/// <summary>
/// O Hub não aceitou o lote. É falha de infraestrutura, não de negócio (ADR-0004) — por isso
/// exceção; quem publica a converte em relatório, porque publicar não pode falhar por causa
/// do aviso.
/// </summary>
public sealed class NotificacaoIndisponivelException : Exception
{
	/// <summary>Construtor sem argumentos, exigido pela convenção de exceções.</summary>
	public NotificacaoIndisponivelException()
		: this("O serviço de notificação está indisponível.")
	{
	}

	/// <summary>Construtor só com mensagem.</summary>
	/// <param name="message">Descrição sem dado sensível (ADR-0020).</param>
	public NotificacaoIndisponivelException(string message)
		: base(message)
	{
	}

	/// <summary>Cria a exceção encadeando a falha original.</summary>
	/// <param name="message">Descrição sem dado sensível (ADR-0020).</param>
	/// <param name="innerException">Falha original.</param>
	public NotificacaoIndisponivelException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}
}
