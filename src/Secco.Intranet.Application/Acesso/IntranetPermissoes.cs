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
		/// <summary>
		/// Família publicada no catálogo (ADR-0043 da plataforma): o SecureGate aceita
		/// <c>setor-{slug}:read</c> de qualquer setor porque casa com ela.
		/// </summary>
		public const string FamiliaRead = "setor-{id}:read";

		/// <summary>Família da escrita por setor (ver <see cref="FamiliaRead"/>).</summary>
		public const string FamiliaWrite = "setor-{id}:write";

		/// <summary>Permissão de leitura de um setor específico.</summary>
		/// <param name="slug">Slug do setor, já normalizado (minúsculo).</param>
		public static string Read(string slug) => SeccoPermissions.FromFamily(FamiliaRead, slug);

		/// <summary>Permissão de escrita de um setor específico.</summary>
		/// <param name="slug">Slug do setor, já normalizado (minúsculo).</param>
		public static string Write(string slug) => SeccoPermissions.FromFamily(FamiliaWrite, slug);

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

	/// <summary>Nome do produto no catálogo de permissões do SecureGate (escopo <c>permissions:intranet</c>).</summary>
	public const string Produto = "intranet";

	/// <summary>
	/// Catálogo publicado no SecureGate pelo <c>migrate</c> (ADR-0039 da plataforma): toda permissão
	/// do produto, com a descrição que quem administra perfis vê. As permissões por setor entram como
	/// <b>famílias</b> (<see cref="Setor.FamiliaRead"/>/<see cref="Setor.FamiliaWrite"/>, ADR-0043 da
	/// plataforma), porque os setores são criados a qualquer momento em cada tenant.
	/// </summary>
	public static IReadOnlyList<SeccoPermissionDefinition> Catalog { get; } =
	[
		new(Setor.ReadGlobal, "Ler todos os setores, inclusive os criados depois"),
		new(Diretorio.Read, "Ver o diretório e editar o próprio contato"),
		new(Diretorio.Manage, "Administrar o diretório: dados funcionais de terceiros e importação"),
		new(Inventario.Read, "Consultar o inventário"),
		new(Setor.FamiliaRead, "Ler um setor específico"),
		new(Setor.FamiliaWrite, "Alterar um setor específico: publicações, documentos e menu"),
	];

	/// <summary>
	/// Nomes fixos do <see cref="Catalog"/>, na mesma ordem — as caixas da tela de edição de perfil.
	/// Família não entra: ela não é permissão atribuível, as instâncias são geradas por setor.
	/// </summary>
	public static readonly IReadOnlyList<string> Catalogo =
		[.. Catalog.Select(permissao => permissao.Name).Where(nome => !SeccoPermissions.IsValidFamily(nome))];
}
