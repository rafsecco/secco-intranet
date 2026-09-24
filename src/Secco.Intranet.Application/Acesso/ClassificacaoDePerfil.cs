using System.Text.RegularExpressions;

namespace Secco.Intranet.Application.Acesso;

/// <summary>
/// Regras puras sobre nome de perfil. A plataforma só conhece o nome da Role; a distinção
/// entre perfil de setor, do produto e comum é convenção desta Intranet (ADR-0001, ADR-0008).
/// </summary>
public static partial class ClassificacaoDePerfil
{
	/// <summary>Superusuário da instalação (o mesmo valor de <c>AcessoAdministrativo.RoleIntranetAdmin</c> no Web).</summary>
	public const string IntranetAdmin = "intranet-admin";

	/// <summary>Administrador do Inventário.</summary>
	public const string InventarioAdmin = "inventario-admin";

	/// <summary>Sufixo de Role de administração de setor.</summary>
	public const string SufixoAdmin = "-admin";

	/// <summary>Sufixo de Role de leitura de setor.</summary>
	public const string SufixoUsuario = "-user";

	/// <summary>Tamanho máximo do nome de um perfil, igual ao da plataforma.</summary>
	public const int TamanhoMaximoDoNome = 100;

	/// <summary>Perfis que o produto conhece e oferece criar quando faltam.</summary>
	public static readonly IReadOnlyList<string> PerfisDoProduto = [IntranetAdmin, InventarioAdmin];

	/// <summary>
	/// Reservados da plataforma — espelho de <c>RoleInputRules.ReservedNames</c> do SecureGate.
	/// A recusa definitiva vem do <c>IsReserved</c> do <c>GetRole</c>; este conjunto só evita a
	/// chamada e esconde o perfil dos seletores.
	/// </summary>
	private static readonly HashSet<string> Reservados = new(StringComparer.OrdinalIgnoreCase)
	{
		"installation-operator",
		"platform-operator",
		"installation-log-reader",
		"installation-auditor",
	};

	[GeneratedRegex("^[a-zA-Z0-9](?:[a-zA-Z0-9._-]*[a-zA-Z0-9])?$", RegexOptions.CultureInvariant)]
	private static partial Regex FormatoDoNome();

	/// <summary>Indica se o perfil é do produto (<c>intranet-admin</c>, <c>inventario-admin</c>).</summary>
	/// <param name="nome">Nome do perfil.</param>
	public static bool EhDoProduto(string nome) =>
		PerfisDoProduto.Any(perfil => string.Equals(perfil, nome, StringComparison.OrdinalIgnoreCase));

	/// <summary>Indica se o perfil é reservado da plataforma.</summary>
	/// <param name="nome">Nome do perfil.</param>
	public static bool EhReservado(string nome) => Reservados.Contains(nome);

	/// <summary>Extrai slug e papel de uma Role de setor; <c>null</c> se não for uma.</summary>
	/// <param name="nome">Nome do perfil.</param>
	public static (string Slug, PapelNoSetor Papel)? DoSetor(string nome)
	{
		if (EhDoProduto(nome))
		{
			return null;
		}

		if (nome.EndsWith(SufixoAdmin, StringComparison.OrdinalIgnoreCase) && nome.Length > SufixoAdmin.Length)
		{
			return (nome[..^SufixoAdmin.Length], PapelNoSetor.Administrador);
		}

		if (nome.EndsWith(SufixoUsuario, StringComparison.OrdinalIgnoreCase) && nome.Length > SufixoUsuario.Length)
		{
			return (nome[..^SufixoUsuario.Length], PapelNoSetor.Usuario);
		}

		return null;
	}

	/// <summary>Classifica o perfil pelo nome.</summary>
	/// <param name="nome">Nome do perfil.</param>
	public static TipoDePerfil Tipo(string nome) =>
		EhDoProduto(nome) ? TipoDePerfil.Produto
		: DoSetor(nome) is not null ? TipoDePerfil.Setor
		: TipoDePerfil.Comum;

	/// <summary>Valida o nome pela regra da plataforma: letras, dígitos, <c>.</c>, <c>_</c> e <c>-</c>, sem espaço.</summary>
	/// <param name="nome">Nome candidato, já aparado.</param>
	public static bool NomeValido(string? nome) =>
		!string.IsNullOrEmpty(nome) && nome.Length <= TamanhoMaximoDoNome && FormatoDoNome().IsMatch(nome);
}
