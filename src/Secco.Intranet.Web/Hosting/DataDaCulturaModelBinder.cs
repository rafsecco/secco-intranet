using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace Secco.Intranet.Web.Hosting;

/// <summary>
/// Lê <see cref="DateOnly"/> e <see cref="DateTime"/> pelo <see cref="FormatoDeDatas"/>: o padrão da
/// cultura configurada (o que as telas mostram) ou ISO (links e campos antigos), sem depender da cultura
/// do servidor nem da requisição.
/// </summary>
/// <param name="formato">Formato configurado.</param>
/// <param name="comHora">Se o alvo é data e hora.</param>
public sealed class DataDaCulturaModelBinder(FormatoDeDatas formato, bool comHora) : IModelBinder
{
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

		if (comHora)
		{
			if (formato.TentarLerDataEHora(texto, out var instante))
			{
				bindingContext.Result = ModelBindingResult.Success(instante);
				return Task.CompletedTask;
			}

			bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, formato.MensagemDeDataEHoraInvalida);
			return Task.CompletedTask;
		}

		if (formato.TentarLerData(texto, out var data))
		{
			bindingContext.Result = ModelBindingResult.Success(data);
			return Task.CompletedTask;
		}

		bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, formato.MensagemDeDataInvalida);
		return Task.CompletedTask;
	}
}

/// <summary>Aplica o <see cref="DataDaCulturaModelBinder"/> a <see cref="DateOnly"/> e <see cref="DateTime"/> (anuláveis ou não).</summary>
public sealed class DataDaCulturaModelBinderProvider : IModelBinderProvider
{
	/// <inheritdoc />
	public IModelBinder? GetBinder(ModelBinderProviderContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var tipo = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;

		if (tipo != typeof(DateOnly) && tipo != typeof(DateTime))
		{
			return null;
		}

		return new DataDaCulturaModelBinder(context.Services.GetRequiredService<FormatoDeDatas>(), comHora: tipo == typeof(DateTime));
	}
}

/// <summary>Registro do <see cref="FormatoDeDatas"/>.</summary>
public static class FormatoDeDatasServiceCollectionExtensions
{
	/// <summary>
	/// Registra o <see cref="FormatoDeDatas"/> com a cultura de <c>Intranet:Formatos:Cultura</c> (padrão
	/// <c>pt-BR</c>) e o leitor de datas dos formulários. Cultura inexistente derruba a subida.
	/// </summary>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="configuration">Configuração da aplicação.</param>
	public static IServiceCollection AddIntranetFormatos(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		var cultura = configuration[FormatoDeDatas.ChaveDaCultura]?.Trim();

		// Lido já no registro, para falhar na subida e não no primeiro formulário.
		services.AddSingleton(new FormatoDeDatas(string.IsNullOrEmpty(cultura) ? FormatoDeDatas.CulturaPadrao : cultura));
		services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(opcoes =>
			opcoes.ModelBinderProviders.Insert(0, new DataDaCulturaModelBinderProvider()));

		return services;
	}
}
