using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Link do menu principal como a tela e o menu o enxergam.</summary>
/// <param name="Id">Id do link.</param>
/// <param name="Chave">Chave de posição no grupo Principal (<c>link-{id}</c>).</param>
/// <param name="Rotulo">Texto exibido.</param>
/// <param name="Url">Destino.</param>
/// <param name="Icone">Classe do Bootstrap Icons; nulo = sem ícone.</param>
/// <param name="NovaAba">Abre em nova aba.</param>
public sealed record LinkDoMenuPrincipalDto(Guid Id, string Chave, string Rotulo, string Url, string? Icone, bool NovaAba)
{
	/// <summary>Projeta a entidade.</summary>
	/// <param name="link">Link de origem.</param>
	public static LinkDoMenuPrincipalDto FromEntity(LinkDoMenuPrincipal link)
	{
		ArgumentNullException.ThrowIfNull(link);

		return new LinkDoMenuPrincipalDto(link.Id, link.Chave, link.Rotulo, link.Url, link.Icone, link.NovaAba);
	}
}

/// <summary>Dados para criar um link do menu principal.</summary>
/// <param name="Rotulo">Texto exibido.</param>
/// <param name="Url">Destino.</param>
/// <param name="Icone">Ícone opcional.</param>
/// <param name="NovaAba">Abre em nova aba.</param>
public sealed record CriarLinkDoMenuPrincipalCommand(string? Rotulo, string? Url, string? Icone, bool NovaAba);

/// <summary>Dados para editar um link do menu principal.</summary>
/// <param name="Id">Link a editar.</param>
/// <param name="Rotulo">Texto exibido.</param>
/// <param name="Url">Destino.</param>
/// <param name="Icone">Ícone opcional.</param>
/// <param name="NovaAba">Abre em nova aba.</param>
public sealed record EditarLinkDoMenuPrincipalCommand(Guid Id, string? Rotulo, string? Url, string? Icone, bool NovaAba);

/// <summary>Campos já validados e aparados de um link do menu principal.</summary>
internal readonly record struct CamposValidadosDoLink(string Rotulo, string Url, string? Icone);

/// <summary>Validação dos campos do link do menu principal; ordem: rótulo, URL, ícone.</summary>
internal static class CamposDeLinkDoMenuPrincipal
{
	// Limites = HasMaxLength de LinkDoMenuPrincipalConfiguration.
	private const int LimiteRotulo = 256;
	private const int LimiteUrl = 512;

	/// <summary>Valida e normaliza; devolve os campos ou o primeiro erro.</summary>
	/// <param name="rotulo">Rótulo informado.</param>
	/// <param name="url">URL informada.</param>
	/// <param name="icone">Ícone informado.</param>
	public static Result<CamposValidadosDoLink> Validar(string? rotulo, string? url, string? icone)
	{
		var rotuloAparado = rotulo?.Trim() ?? string.Empty;

		if (rotuloAparado.Length == 0)
		{
			return Result.Failure<CamposValidadosDoLink>(IntranetErrors.LinksDoMenu.RotuloRequired);
		}

		if (rotuloAparado.Length > LimiteRotulo)
		{
			return Result.Failure<CamposValidadosDoLink>(IntranetErrors.LinksDoMenu.RotuloMuitoLongo);
		}

		var urlAparada = url?.Trim() ?? string.Empty;

		if (urlAparada.Length == 0)
		{
			return Result.Failure<CamposValidadosDoLink>(IntranetErrors.LinksDoMenu.UrlRequired);
		}

		if (urlAparada.Length > LimiteUrl)
		{
			return Result.Failure<CamposValidadosDoLink>(IntranetErrors.LinksDoMenu.UrlMuitoLonga);
		}

		if (!CriarItemMenuHandler.RotaEhValida(urlAparada))
		{
			return Result.Failure<CamposValidadosDoLink>(IntranetErrors.Menu.RotaInvalida);
		}

		if (!IconeBootstrap.TentarNormalizar(icone, out var iconeNormalizado))
		{
			return Result.Failure<CamposValidadosDoLink>(IntranetErrors.Menu.IconeInvalido);
		}

		return new CamposValidadosDoLink(rotuloAparado, urlAparada, iconeNormalizado);
	}
}
