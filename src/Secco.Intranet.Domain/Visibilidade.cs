namespace Secco.Intranet.Domain;

/// <summary>
/// Quem enxerga um conteúdo publicado por um setor. É o conceito de visibilidade do produto:
/// documentos e publicações do mural usam o mesmo, porque significam a mesma coisa.
/// </summary>
public enum Visibilidade
{
	/// <summary>Apenas quem tem a Role do setor dono (<c>{slug}-admin</c> ou <c>{slug}-user</c>).</summary>
	Setor = 0,

	/// <summary>Qualquer pessoa autenticada da instituição.</summary>
	Empresa = 1,
}
