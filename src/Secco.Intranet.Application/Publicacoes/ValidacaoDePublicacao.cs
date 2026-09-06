using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>
/// Validação de entrada compartilhada por publicar e editar. Existe para as duas operações não
/// divergirem: uma regra aplicada só na criação vira porta aberta na edição.
/// </summary>
internal static class ValidacaoDePublicacao
{
	/// <summary>Limite da coluna <c>ds_corpo</c>.</summary>
	internal const int LimiteDoCorpo = 4_000;

	/// <summary>Devolve o erro correspondente, ou <c>null</c> quando a entrada é válida.</summary>
	/// <param name="titulo">Título informado.</param>
	/// <param name="corpo">Corpo informado.</param>
	/// <param name="publicadoEm">Entrada no ar.</param>
	/// <param name="expiraEm">Expiração.</param>
	/// <param name="limiteDoTitulo">Limite configurado para o título.</param>
	internal static Error? Validar(
		string? titulo,
		string? corpo,
		DateTimeOffset publicadoEm,
		DateTimeOffset? expiraEm,
		int limiteDoTitulo)
	{
		if (string.IsNullOrWhiteSpace(titulo))
		{
			return IntranetErrors.Publicacoes.TituloRequired;
		}

		if (titulo.Length > limiteDoTitulo)
		{
			return IntranetErrors.Publicacoes.TituloTooLong(limiteDoTitulo);
		}

		if (string.IsNullOrWhiteSpace(corpo))
		{
			return IntranetErrors.Publicacoes.CorpoRequired;
		}

		if (corpo.Length > LimiteDoCorpo)
		{
			return IntranetErrors.Publicacoes.CorpoTooLong;
		}

		return expiraEm is not null && expiraEm <= publicadoEm
			? IntranetErrors.Publicacoes.ExpiracaoInvalida
			: null;
	}
}
