using Secco.Intranet.Application.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Links;

/// <summary>Validação dos campos de texto de links e grupos — mesmos limites e regra de rota do menu.</summary>
internal static class CamposDeLink
{
	// Limites = HasMaxLength de ItemMenuConfiguration, os mesmos de CriarItemMenuHandler.
	private const int LimiteNome = 256;
	private const int LimiteRota = 512;

	/// <summary>Valida o nome; devolve o erro, ou nulo quando está certo.</summary>
	/// <param name="nome">Nome informado.</param>
	public static Error? ValidarNome(string? nome)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			return IntranetErrors.Menu.NomeRequired;
		}

		return nome.Trim().Length > LimiteNome ? IntranetErrors.Menu.CampoMuitoLongo("O nome", LimiteNome) : null;
	}

	/// <summary>Apara e valida o endereço; devolve o erro, ou nulo quando está certo.</summary>
	/// <param name="rota">Endereço informado.</param>
	/// <param name="endereco">Endereço aparado.</param>
	public static Error? ValidarEndereco(string? rota, out string endereco)
	{
		endereco = rota?.Trim() ?? string.Empty;

		if (endereco.Length == 0)
		{
			return IntranetErrors.Links.EnderecoRequired;
		}

		if (endereco.Length > LimiteRota)
		{
			return IntranetErrors.Menu.CampoMuitoLongo("O endereço", LimiteRota);
		}

		return CriarItemMenuHandler.RotaEhValida(endereco) ? null : IntranetErrors.Menu.RotaInvalida;
	}
}
