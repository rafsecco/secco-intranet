using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>
/// Validação de entrada compartilhada por publicar e editar. Existe para as duas operações não
/// divergirem: uma regra aplicada só na criação vira porta aberta na edição.
/// </summary>
internal static class ValidacaoDePublicacao
{
	/// <summary>Limite da coluna <c>ds_corpo</c>.</summary>
	internal const int LimiteDoCorpo = 20_000;

	/// <summary>Limite de células estimadas de tabela no corpo.</summary>
	internal const int LimiteDeCelulasDeTabela = 10_000;

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

		if (corpo is not null && corpo.Length > LimiteDoCorpo)
		{
			return IntranetErrors.Publicacoes.CorpoTooLong;
		}

		if (corpo is not null && EstimarCelulasDeTabela(corpo) > LimiteDeCelulasDeTabela)
		{
			return IntranetErrors.Publicacoes.TabelaGrandeDemais;
		}

		return expiraEm is not null && expiraEm <= publicadoEm
			? IntranetErrors.Publicacoes.ExpiracaoInvalida
			: null;
	}

	/// <summary>
	/// Estima as células que a tabela em Markdown renderizaria: (maior número de pipes numa
	/// linha + 1) x linhas. A tabela completa cada linha até a largura do cabeçalho, então o
	/// pior caso é esse produto. Varredura linear dos caracteres, sem parsear o Markdown, que
	/// é o custo que se quer evitar. Devolve 0 quando não há pipe.
	/// </summary>
	private static long EstimarCelulasDeTabela(string corpo)
	{
		var maiorNumeroDePipes = 0;
		var pipesNaLinha = 0;
		long linhas = 1;

		for (var i = 0; i < corpo.Length; i++)
		{
			var caractere = corpo[i];

			if (caractere == '|')
			{
				pipesNaLinha++;
			}
			else if (caractere == '\n' || caractere == '\r')
			{
				// O Markdig trata \r, \n e \r\n como fim de linha; \r\n é uma quebra só.
				if (caractere == '\r' && i + 1 < corpo.Length && corpo[i + 1] == '\n')
				{
					i++;
				}

				linhas++;
				maiorNumeroDePipes = Math.Max(maiorNumeroDePipes, pipesNaLinha);
				pipesNaLinha = 0;
			}
		}

		maiorNumeroDePipes = Math.Max(maiorNumeroDePipes, pipesNaLinha);

		return maiorNumeroDePipes == 0 ? 0 : (maiorNumeroDePipes + 1L) * linhas;
	}
}
