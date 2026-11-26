using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Secco.Intranet.Web.Hosting;

/// <summary>
/// Lê <see cref="DateOnly"/> no formato brasileiro (<c>dd/mm/aaaa</c>), que é como as telas mostram
/// e pedem datas, e também no formato ISO (<c>aaaa-mm-dd</c>) dos links e dos campos de data nativos.
/// Não depende da cultura do servidor: o contêiner Linux roda em cultura invariante.
/// </summary>
public sealed class DataBrasileiraModelBinder : IModelBinder
{
	/// <summary>Mensagem de data que não pôde ser lida.</summary>
	public const string MensagemDeDataInvalida = "Informe uma data válida no formato dd/mm/aaaa.";

	private static readonly string[] Formatos = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];

	/// <inheritdoc />
	public Task BindModelAsync(ModelBindingContext bindingContext)
	{
		ArgumentNullException.ThrowIfNull(bindingContext);

		var valor = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

		if (valor == ValueProviderResult.None)
		{
			return Task.CompletedTask;
		}

		bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valor);

		var texto = valor.FirstValue?.Trim();

		if (string.IsNullOrEmpty(texto))
		{
			return Task.CompletedTask;
		}

		if (DateOnly.TryParseExact(texto, Formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
		{
			bindingContext.Result = ModelBindingResult.Success(data);
		}
		else
		{
			bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, MensagemDeDataInvalida);
		}

		return Task.CompletedTask;
	}
}

/// <summary>Aplica o <see cref="DataBrasileiraModelBinder"/> a <see cref="DateOnly"/> e <see cref="Nullable{DateOnly}"/>.</summary>
public sealed class DataBrasileiraModelBinderProvider : IModelBinderProvider
{
	/// <inheritdoc />
	public IModelBinder? GetBinder(ModelBinderProviderContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var tipo = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;

		return tipo == typeof(DateOnly) ? new DataBrasileiraModelBinder() : null;
	}
}
