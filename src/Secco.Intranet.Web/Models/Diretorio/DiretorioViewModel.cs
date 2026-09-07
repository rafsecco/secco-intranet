namespace Secco.Intranet.Web.Models.Diretorio;

/// <summary>Uma pessoa no diretório organizacional.</summary>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Cargo">Cargo.</param>
/// <param name="SetorNome">Setor ao qual pertence.</param>
/// <param name="SetorSlug">Slug do setor, que define a cor.</param>
/// <param name="Email">Endereço de e-mail interno.</param>
/// <param name="Ramal">Ramal telefônico.</param>
public sealed record PessoaViewModel(
	string Nome,
	string Cargo,
	string SetorNome,
	string SetorSlug,
	string Email,
	string Ramal)
{
	/// <summary>Iniciais para o avatar.</summary>
	public string Iniciais
	{
		get
		{
			var partes = Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);

			return partes.Length < 2
				? Nome[..1].ToUpperInvariant()
				: string.Concat(partes[0][..1], partes[^1][..1]).ToUpperInvariant();
		}
	}
}

/// <summary>Modelo da grade de pessoas.</summary>
/// <param name="Pessoas">Pessoas listadas.</param>
/// <param name="Busca">Termo buscado, para re-popular o campo.</param>
public sealed record DiretorioViewModel(IReadOnlyList<PessoaViewModel> Pessoas, string? Busca);
