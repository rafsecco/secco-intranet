namespace Secco.Intranet.Infrastructure.Armazenamento;

/// <summary>
/// Falha de configuração ou de integridade da cifragem de documentos. É sempre um defeito de
/// infraestrutura ou uma adulteração — nunca um erro de negócio, que fluiria por <c>Result</c>.
/// </summary>
public sealed class ChaveMestraException : Exception
{
	/// <summary>Cria a exceção sem mensagem específica.</summary>
	public ChaveMestraException()
		: base("Falha na cifragem de documentos.")
	{
	}

	/// <summary>Cria a exceção com a mensagem informada.</summary>
	/// <param name="message">Descrição da falha, sem detalhe sensível.</param>
	public ChaveMestraException(string message)
		: base(message)
	{
	}

	/// <summary>Cria a exceção com mensagem e causa.</summary>
	/// <param name="message">Descrição da falha, sem detalhe sensível.</param>
	/// <param name="innerException">Causa original.</param>
	public ChaveMestraException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
