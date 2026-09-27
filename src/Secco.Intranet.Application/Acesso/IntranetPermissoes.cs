using Secco.SharedKernel.Authorization;

namespace Secco.Intranet.Application.Acesso;

/// <summary>
/// Catálogo de permissões do produto, no formato canônico da plataforma (<c>recurso:acao</c>,
/// ADR-0021). Ações em inglês (<c>read</c>/<c>write</c>/<c>manage</c>) — decisão de 2026-09-27, para
/// bater com os nomes já prometidos nas specs do Diretório e da Área administrativa. Fixo: a tela de
/// edição de perfil oferece lista, nunca texto livre (ver <see cref="Catalogo"/>).
/// </summary>
public static class IntranetPermissoes
{
	/// <summary>Permissões de setor — a por-slug é dinâmica, a global cobre "todos, inclusive futuros".</summary>
	public static class Setor
	{
		/// <summary>Permissão de leitura de um setor específico.</summary>
		/// <param name="slug">Slug do setor, já normalizado (minúsculo).</param>
		public static string Read(string slug) => SeccoPermissions.Create($"setor-{slug}", "read");

		/// <summary>Permissão de escrita de um setor específico.</summary>
		/// <param name="slug">Slug do setor, já normalizado (minúsculo).</param>
		public static string Write(string slug) => SeccoPermissions.Create($"setor-{slug}", "write");

		/// <summary>Leitura de todo setor do tenant, inclusive os criados depois da atribuição.</summary>
		public const string ReadGlobal = "setores:read";
	}

	/// <summary>Permissões do Diretório organizacional — nomes já fixados na spec de 2026-09-24.</summary>
	public static class Diretorio
	{
		/// <summary>Ver o diretório e editar o próprio contato.</summary>
		public const string Read = "diretorio:read";

		/// <summary>Tudo, inclusive dados funcionais de terceiros e importação.</summary>
		public const string Manage = "diretorio:manage";
	}

	/// <summary>Permissões do Inventário — administrar continua por nome de Role (<c>inventario-admin</c>).</summary>
	public static class Inventario
	{
		/// <summary>Nível de só-consulta, além do administrativo.</summary>
		public const string Read = "inventario:read";
	}

	/// <summary>
	/// Permissões fixas oferecidas pela tela de edição de perfil. Não inclui
	/// <see cref="Setor.Read(string)"/>/<see cref="Setor.Write(string)"/> — essas são geradas por
	/// setor existente, não uma lista fixa.
	/// </summary>
	public static readonly IReadOnlyList<string> Catalogo =
	[
		Setor.ReadGlobal,
		Diretorio.Read,
		Diretorio.Manage,
		Inventario.Read,
	];
}
