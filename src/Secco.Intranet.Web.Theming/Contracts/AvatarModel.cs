namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Tamanho do avatar.</summary>
public enum AvatarTamanho
{
	/// <summary>Tamanho de listas e menus.</summary>
	Padrao = 0,

	/// <summary>Tamanho de cartões e cabeçalhos de perfil.</summary>
	Grande = 1,
}

/// <summary>Identificação visual de uma pessoa: a foto, ou as iniciais quando não há foto.</summary>
/// <param name="Iniciais">Iniciais exibidas quando não há foto.</param>
/// <param name="FotoUrl">Endereço da foto; nulo quando a pessoa não tem foto.</param>
/// <param name="Tamanho">Tamanho do avatar.</param>
public sealed record AvatarModel(string Iniciais, string? FotoUrl, AvatarTamanho Tamanho = AvatarTamanho.Padrao);
