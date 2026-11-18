namespace Secco.Intranet.Domain.Salas;

/// <summary>Política de espera entre tentativas de falhas passageiras, compartilhada pelo espelhamento e pela exclusão de eventos.</summary>
internal static class EsperaDeTentativas
{
	/// <summary>Tamanho máximo gravado para o motivo de uma falha.</summary>
	internal const int MotivoMaximo = 500;

	/// <summary>Espera, em minutos, após a falha de número 1, 2, 3... A última posição é a falha que esgota as tentativas.</summary>
	private static readonly int[] Minutos = [1, 2, 4, 8, 16, 32, 60, 60];

	/// <summary>Espera até a próxima tentativa após a falha de número <paramref name="numeroDaFalha"/>; <c>null</c> quando as tentativas se esgotaram.</summary>
	internal static TimeSpan? Apos(int numeroDaFalha) =>
		numeroDaFalha >= Minutos.Length ? null : TimeSpan.FromMinutes(Minutos[numeroDaFalha - 1]);

	/// <summary>Corta o motivo no tamanho máximo gravado.</summary>
	internal static string Cortar(string motivo) =>
		motivo.Length > MotivoMaximo ? motivo[..MotivoMaximo] : motivo;
}
