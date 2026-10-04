namespace Secco.Intranet.Application.Setores;

/// <summary>
/// Primeiros segmentos de URL que um setor não pode usar como slug: o setor mora na raiz
/// (<c>/{slug}/…</c>) e as rotas fixas do produto vencem a dele. <c>SlugsReservadosTests</c>
/// confere esta lista contra todos os endpoints — controller novo precisa entrar aqui.
/// </summary>
public static class SlugsReservados
{
	/// <summary>Todos os slugs reservados, sem diferenciar caixa.</summary>
	public static IReadOnlySet<string> Todos { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		// Controllers (rota de atributo e convencional).
		"acesso", "conta", "diretorio", "documentos", "home", "inventario", "mural", "publicacoes", "setores",
		// Prefixo antigo da página do setor: reservado para nunca virar um setor que confunda links velhos.
		"setor",
		// Infraestrutura e arquivos estáticos.
		"health", "_content", "css", "js", "lib", "img", "favicon.ico", "api", ".well-known",
		// Callbacks do OpenID Connect: o middleware de autenticação responde antes do roteamento,
		// então não aparecem entre os endpoints — e o teste estrutural não os enxerga.
		"signin-oidc", "signout-callback-oidc",
	};

	/// <summary>Se o slug (já aparado) é reservado.</summary>
	/// <param name="slug">Slug candidato.</param>
	public static bool Contem(string? slug) => slug is not null && Todos.Contains(slug.Trim());
}
