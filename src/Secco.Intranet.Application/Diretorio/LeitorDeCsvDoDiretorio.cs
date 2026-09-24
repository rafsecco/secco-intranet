using System.Text;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Uma linha lida do CSV. Célula vazia vira <c>null</c>, que na importação significa "não alterar".</summary>
/// <param name="Numero">Número do registro no arquivo (o cabeçalho é 1).</param>
/// <param name="Email">E-mail do usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Cargo">Cargo.</param>
/// <param name="Ramal">Ramal.</param>
/// <param name="SetorSlug">Slug do setor de lotação.</param>
/// <param name="GestorEmail">E-mail do gestor.</param>
public sealed record LinhaDoCsv(int Numero, string Email, string? Nome, string? Cargo, string? Ramal, string? SetorSlug, string? GestorEmail);

/// <summary>Erro de uma linha (o resto do arquivo segue valendo).</summary>
/// <param name="Numero">Número do registro no arquivo.</param>
/// <param name="Mensagem">O que está errado.</param>
public sealed record ErroDeLinhaDoCsv(int Numero, string Mensagem);

/// <summary>Resultado da leitura: as linhas boas e os erros de linha.</summary>
/// <param name="Linhas">Linhas lidas.</param>
/// <param name="Erros">Erros por linha.</param>
public sealed record LeituraDoCsv(IReadOnlyList<LinhaDoCsv> Linhas, IReadOnlyList<ErroDeLinhaDoCsv> Erros);

/// <summary>
/// Leitor do CSV do diretório. Cabeçalho <c>email;nome;cargo;ramal;setor;gestor</c> (só <c>email</c>
/// é obrigatório), delimitador <c>;</c> ou <c>,</c> detectado no cabeçalho, aspas do padrão CSV,
/// BOM tolerado. O conteúdo é tratado como texto puro: nada é interpretado ou avaliado. Erro do
/// <b>arquivo</b> (cabeçalho, limites, aspas abertas) derruba a leitura; erro de <b>linha</b> vira
/// item de <see cref="LeituraDoCsv.Erros"/>.
/// </summary>
public static class LeitorDeCsvDoDiretorio
{
	/// <summary>Máximo de linhas de dados.</summary>
	public const int MaximoDeLinhas = 5_000;

	/// <summary>Máximo de caracteres do arquivo (1 MB de texto).</summary>
	public const int MaximoDeCaracteres = 1_048_576;

	private static readonly string[] Conhecidas = ["email", "nome", "cargo", "ramal", "setor", "gestor"];

	/// <summary>Lê o texto do CSV.</summary>
	/// <param name="texto">Conteúdo do arquivo, já decodificado.</param>
	public static Result<LeituraDoCsv> Ler(string? texto)
	{
		if (string.IsNullOrWhiteSpace(texto))
		{
			return Falha("o arquivo está vazio.");
		}

		if (texto.Length > MaximoDeCaracteres)
		{
			return Falha("o arquivo passa do limite de 1 MB.");
		}

		var registros = Dividir(texto.TrimStart('﻿'), out var abertas);

		if (abertas)
		{
			return Falha("há aspas que não foram fechadas.");
		}

		var naoVazios = registros.Where(registro => registro.Celulas.Any(celula => !string.IsNullOrWhiteSpace(celula))).ToList();

		if (naoVazios.Count == 0)
		{
			return Falha("o arquivo está vazio.");
		}

		var cabecalho = naoVazios[0].Celulas.Select(celula => celula.Trim().ToLowerInvariant()).ToList();
		var desconhecidas = cabecalho.Where(coluna => coluna.Length > 0 && !Conhecidas.Contains(coluna)).ToList();

		if (desconhecidas.Count > 0)
		{
			return Falha($"coluna desconhecida: {string.Join(", ", desconhecidas)}. Use: {string.Join(", ", Conhecidas)}.");
		}

		if (cabecalho.Where(coluna => coluna.Length > 0).GroupBy(coluna => coluna).Any(grupo => grupo.Count() > 1))
		{
			return Falha("há colunas repetidas no cabeçalho.");
		}

		var indiceDoEmail = cabecalho.IndexOf("email");

		if (indiceDoEmail < 0)
		{
			return Falha("falta a coluna email no cabeçalho.");
		}

		var dados = naoVazios.Skip(1).ToList();

		if (dados.Count > MaximoDeLinhas)
		{
			return Falha($"o arquivo tem mais de {MaximoDeLinhas} linhas.");
		}

		var linhas = new List<LinhaDoCsv>();
		var erros = new List<ErroDeLinhaDoCsv>();

		foreach (var (numero, celulas) in dados)
		{
			if (celulas.Count > cabecalho.Count)
			{
				erros.Add(new ErroDeLinhaDoCsv(numero, "a linha tem mais colunas que o cabeçalho."));

				continue;
			}

			string? Valor(string coluna)
			{
				var indice = cabecalho.IndexOf(coluna);

				return indice >= 0 && indice < celulas.Count && !string.IsNullOrWhiteSpace(celulas[indice])
					? celulas[indice].Trim()
					: null;
			}

			var email = Valor("email");

			if (email is null)
			{
				erros.Add(new ErroDeLinhaDoCsv(numero, "o e-mail é obrigatório."));

				continue;
			}

			linhas.Add(new LinhaDoCsv(numero, email, Valor("nome"), Valor("cargo"), Valor("ramal"), Valor("setor"), Valor("gestor")));
		}

		return new LeituraDoCsv(linhas, erros);
	}

	private static Result<LeituraDoCsv> Falha(string motivo) =>
		Result.Failure<LeituraDoCsv>(IntranetErrors.Diretorio.CsvInvalido(motivo));

	private static List<(int Numero, List<string> Celulas)> Dividir(string texto, out bool aspasAbertas)
	{
		var delimitador = DetectarDelimitador(texto);
		var registros = new List<(int, List<string>)>();
		var celulas = new List<string>();
		var atual = new StringBuilder();
		var entreAspas = false;
		var numero = 1;

		for (var i = 0; i < texto.Length; i++)
		{
			var c = texto[i];

			if (entreAspas)
			{
				if (c == '"')
				{
					if (i + 1 < texto.Length && texto[i + 1] == '"')
					{
						atual.Append('"');
						i++;
					}
					else
					{
						entreAspas = false;
					}
				}
				else
				{
					atual.Append(c);
				}
			}
			else if (c == '"' && atual.Length == 0)
			{
				entreAspas = true;
			}
			else if (c == delimitador)
			{
				celulas.Add(atual.ToString());
				atual.Clear();
			}
			else if (c == '\n')
			{
				celulas.Add(atual.ToString());
				atual.Clear();
				registros.Add((numero, celulas));
				celulas = [];
				numero++;
			}
			else if (c != '\r')
			{
				atual.Append(c);
			}
		}

		if (atual.Length > 0 || celulas.Count > 0)
		{
			celulas.Add(atual.ToString());
			registros.Add((numero, celulas));
		}

		aspasAbertas = entreAspas;

		return registros;
	}

	private static char DetectarDelimitador(string texto)
	{
		var fim = texto.IndexOf('\n');
		var primeiraLinha = fim < 0 ? texto : texto[..fim];
		var pontoEVirgula = primeiraLinha.Count(c => c == ';');
		var virgula = primeiraLinha.Count(c => c == ',');

		return pontoEVirgula > 0 && pontoEVirgula >= virgula ? ';' : ',';
	}
}
