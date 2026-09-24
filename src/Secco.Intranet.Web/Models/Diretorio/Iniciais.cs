namespace Secco.Intranet.Web.Models.Diretorio;

/// <summary>Iniciais para o avatar (substituídas pela foto na etapa 6).</summary>
public static class Iniciais
{
	/// <summary>Primeira e última iniciais do nome, em maiúsculas; um nome só dá uma letra.</summary>
	/// <param name="nome">Nome de exibição (ou e-mail).</param>
	public static string De(string nome)
	{
		var partes = nome.Split([' ', '@', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		return partes.Length switch
		{
			0 => "?",
			1 => partes[0][..1].ToUpperInvariant(),
			_ => string.Concat(partes[0][..1], partes[^1][..1]).ToUpperInvariant(),
		};
	}
}
