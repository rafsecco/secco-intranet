using System.Security.Cryptography;
using System.Text;

namespace Secco.Intranet.Application;

/// <summary>Validador (<c>ETag</c>) de um arquivo guardado, derivado do caminho sem expô-lo.</summary>
public static class ETagDeArquivo
{
	/// <summary>
	/// Calcula o validador entre aspas. Cada gravação gera um caminho novo, então o valor muda a cada
	/// versão do arquivo.
	/// </summary>
	/// <param name="caminhoRelativo">Endereço do arquivo no armazenamento.</param>
	public static string De(string caminhoRelativo)
	{
		ArgumentNullException.ThrowIfNull(caminhoRelativo);

		var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(caminhoRelativo)))[..16];

		return $"\"{hash}\"";
	}
}
