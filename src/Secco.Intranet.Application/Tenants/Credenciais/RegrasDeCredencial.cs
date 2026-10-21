using System.Text.RegularExpressions;

namespace Secco.Intranet.Application.Tenants.Credenciais;

/// <summary>Regras de formato e limites das credenciais, iguais às da plataforma.</summary>
public static partial class RegrasDeCredencial
{
	/// <summary>Tamanho máximo do nome.</summary>
	public const int NomeMaximo = 100;

	/// <summary>Máximo de perfis por credencial.</summary>
	public const int PerfisMaximo = 20;

	/// <summary>Máximo de endereços de retorno (e, separadamente, de logout).</summary>
	public const int EnderecosMaximo = 10;

	/// <summary>Tamanho máximo de cada endereço.</summary>
	public const int EnderecoMaximo = 2000;

	/// <summary>Se o texto tem o formato de um client id (<c>cli_</c> + 16 caracteres base32 minúsculos).</summary>
	/// <param name="clientId">Texto vindo da rota.</param>
	public static bool ClientIdValido(string? clientId) => clientId is not null && ClientIdRegex().IsMatch(clientId);

	/// <summary>Se o nome, já aparado, tem de 1 a 100 caracteres e nenhum de controle.</summary>
	/// <param name="nome">Nome aparado.</param>
	public static bool NomeValido(string nome) =>
		nome.Length is >= 1 and <= NomeMaximo && !nome.Any(char.IsControl);

	/// <summary>Separa o texto por linha: apara, descarta vazias e duplicadas (ordinal) e mantém a ordem.</summary>
	/// <param name="texto">Texto do campo.</param>
	public static IReadOnlyList<string> Linhas(string? texto)
	{
		if (string.IsNullOrWhiteSpace(texto))
		{
			return [];
		}

		var vistas = new HashSet<string>(StringComparer.Ordinal);
		var linhas = new List<string>();

		foreach (var bruta in texto.Split('\n'))
		{
			var linha = bruta.Trim();

			if (linha.Length > 0 && vistas.Add(linha))
			{
				linhas.Add(linha);
			}
		}

		return linhas;
	}

	/// <summary>Se o endereço é absoluto, sem user-info nem fragmento, dentro do limite e <c>https</c> (ou <c>http</c> se permitido).</summary>
	/// <param name="uri">Endereço.</param>
	/// <param name="permitirHttp">Se <c>http</c> é aceito.</param>
	public static bool EnderecoValido(string uri, bool permitirHttp)
	{
		if (uri.Length > EnderecoMaximo
			|| uri.Contains('#', StringComparison.Ordinal)
			|| !Uri.TryCreate(uri, UriKind.Absolute, out var u)
			|| u.UserInfo.Length != 0)
		{
			return false;
		}

		return u.Scheme == Uri.UriSchemeHttps || (permitirHttp && u.Scheme == Uri.UriSchemeHttp);
	}

	/// <summary>Produtos dos recursos com banco que estão ligados (inclusive os que não responderam).</summary>
	/// <param name="recursos">Recursos do tenant.</param>
	public static IReadOnlyList<string> EscoposLigados(IEnumerable<RecursoDoTenantDto> recursos)
	{
		ArgumentNullException.ThrowIfNull(recursos);

		return recursos
			.Where(r => r.Situacao != SituacaoDoRecurso.NaoLigado)
			.Select(r => RecursosDaPlataforma.Produto(r.Recurso))
			.OfType<string>()
			.ToList();
	}

	/// <summary>Se o SecureGate está ligado no tenant.</summary>
	/// <param name="recursos">Recursos do tenant.</param>
	public static bool SecureGateLigado(IEnumerable<RecursoDoTenantDto> recursos)
	{
		ArgumentNullException.ThrowIfNull(recursos);

		return recursos.Any(r => r.Recurso == RecursoDaPlataforma.SecureGate && r.Situacao != SituacaoDoRecurso.NaoLigado);
	}

	[GeneratedRegex(@"^cli_[a-z2-7]{16}\z", RegexOptions.CultureInvariant)]
	private static partial Regex ClientIdRegex();
}
