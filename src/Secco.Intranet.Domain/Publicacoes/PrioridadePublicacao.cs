namespace Secco.Intranet.Domain.Publicacoes;

/// <summary>
/// Urgência da publicação. Quem publica escolhe a urgência, não os canais: traduzir urgência
/// em canais é decisão do produto, e fica fora do domínio.
/// </summary>
public enum PrioridadePublicacao
{
	/// <summary>Aparece no mural e no sino.</summary>
	Normal = 0,

	/// <summary>Além do sino, alcança o e-mail de quem pode ver.</summary>
	Importante = 1,

	/// <summary>Como <see cref="Importante"/>, e com destaque próprio no mural.</summary>
	Urgente = 2,
}
