using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Menu;

/// <summary>
/// Link externo ou interno que o administrador põe no menu principal do tenant. A validação de
/// formato (URL aceita, ícone) é da Application; aqui só a invariante de não ficar vazio.
/// </summary>
public sealed class LinkDoMenuPrincipal : BaseEntity
{
	/// <summary>Prefixo da chave de posição do link no grupo <see cref="GrupoDoMenu.Principal"/>.</summary>
	public const string PrefixoDaChave = "link-";

	private LinkDoMenuPrincipal()
	{
		// Construtor de rehidratação do EF Core
		Rotulo = string.Empty;
		Url = string.Empty;
	}

	/// <summary>Cria um link do menu principal.</summary>
	/// <param name="rotulo">Texto exibido no menu.</param>
	/// <param name="url">Destino: caminho local ou URL absoluta http(s).</param>
	/// <param name="icone">Classe do Bootstrap Icons; nulo = sem ícone.</param>
	/// <param name="novaAba">Abre o destino em nova aba.</param>
	/// <exception cref="DomainInvariantException">Rótulo ou URL vazios.</exception>
	public LinkDoMenuPrincipal(string rotulo, string url, string? icone, bool novaAba)
	{
		Validar(rotulo, url);

		Rotulo = rotulo;
		Url = url;
		Icone = icone;
		NovaAba = novaAba;
	}

	/// <summary>Texto exibido no menu.</summary>
	public string Rotulo { get; private set; }

	/// <summary>Destino do link.</summary>
	public string Url { get; private set; }

	/// <summary>Classe do Bootstrap Icons; nulo = sem ícone.</summary>
	public string? Icone { get; private set; }

	/// <summary>Abre o destino em nova aba.</summary>
	public bool NovaAba { get; private set; }

	/// <summary>Chave do link no grupo Principal do menu: prefixo mais o Id em formato <c>D</c>.</summary>
	public string Chave => PrefixoDaChave + Id.ToString("D");

	/// <summary>Troca os quatro campos editáveis.</summary>
	/// <param name="rotulo">Novo rótulo.</param>
	/// <param name="url">Novo destino.</param>
	/// <param name="icone">Novo ícone; nulo = sem ícone.</param>
	/// <param name="novaAba">Abre em nova aba.</param>
	/// <exception cref="DomainInvariantException">Rótulo ou URL vazios.</exception>
	public void Editar(string rotulo, string url, string? icone, bool novaAba)
	{
		Validar(rotulo, url);

		Rotulo = rotulo;
		Url = url;
		Icone = icone;
		NovaAba = novaAba;
	}

	private static void Validar(string rotulo, string url)
	{
		if (string.IsNullOrWhiteSpace(rotulo))
		{
			throw new DomainInvariantException("Um link do menu exige rótulo não vazio.");
		}

		if (string.IsNullOrWhiteSpace(url))
		{
			throw new DomainInvariantException("Um link do menu exige URL não vazia.");
		}
	}
}
