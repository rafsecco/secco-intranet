using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Secco.Intranet.Web.Hosting;

/// <summary>
/// Valor de um campo de data digitado: o texto enviado, quando ele não pôde ser lido (para a pessoa
/// corrigir o que digitou); senão o valor do modelo no formato configurado.
/// </summary>
public static class CampoDeData
{
	/// <summary>Valor para o atributo <c>value</c> do campo.</summary>
	/// <param name="estado">Estado do modelo da requisição.</param>
	/// <param name="chave">Nome do campo (ex.: <c>Form.DataVencimento</c>).</param>
	/// <param name="formatado">Valor do modelo já formatado pelo <see cref="FormatoDeDatas"/>.</param>
	public static string Valor(ModelStateDictionary estado, string chave, string formatado)
	{
		ArgumentNullException.ThrowIfNull(estado);

		return estado.TryGetValue(chave, out var entrada) && entrada.Errors.Count > 0 && entrada.AttemptedValue is { } digitado
			? digitado
			: formatado;
	}
}
