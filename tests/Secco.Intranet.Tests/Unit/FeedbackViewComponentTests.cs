using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Secco.Intranet.Web.Theming.Contracts;
using Secco.Intranet.Web.ViewComponents;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// O feedback de ação saiu das views de página e virou parte do contrato de tema: o layout
/// invoca este componente em toda página, então ele precisa ficar calado quando não há nada
/// a dizer.
/// </summary>
public class FeedbackViewComponentTests
{
	private static (FeedbackViewComponent Componente, ITempDataDictionary TempData, ProvedorDeTempData Provedor) Montar()
	{
		var provedor = new ProvedorDeTempData();
		var tempData = new TempDataDictionary(new DefaultHttpContext(), provedor);

		var componente = new FeedbackViewComponent
		{
			ViewComponentContext = new ViewComponentContext
			{
				ViewContext = new ViewContext
				{
					TempData = tempData,
					ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
				},
			},
		};

		return (componente, tempData, provedor);
	}

	[Fact]
	public void SemMensagem_NaoRenderizaNada()
	{
		var (componente, _, _) = Montar();

		componente.Invoke()
			.Should().BeOfType<ContentViewComponentResult>()
			.Which.Content.Should().BeEmpty(
				"o layout invoca o componente em toda página, e a maioria não tem o que avisar");
	}

	[Fact]
	public void MensagemEmBranco_NaoRenderizaNada()
	{
		var (componente, tempData, _) = Montar();
		tempData[FeedbackViewComponent.ChaveDaMensagem] = "   ";

		componente.Invoke().Should().BeOfType<ContentViewComponentResult>();
	}

	[Fact]
	public void ComMensagem_EntregaOTextoAoTemaComoSucesso()
	{
		var (componente, tempData, _) = Montar();
		tempData[FeedbackViewComponent.ChaveDaMensagem] = "Setor \"Compras\" salvo.";

		var model = componente.Invoke()
			.Should().BeOfType<ViewViewComponentResult>()
			.Which.ViewData!.Model.Should().BeOfType<ToastModel>().Subject;

		model.Texto.Should().Be("Setor \"Compras\" salvo.");
		model.Variante.Should().Be(ToastVariante.Sucesso);
	}

	[Fact]
	public void LerConsomeAMensagem()
	{
		var (componente, tempData, provedor) = Montar();
		tempData[FeedbackViewComponent.ChaveDaMensagem] = "Documento arquivado.";

		componente.Invoke().Should().BeOfType<ViewViewComponentResult>();
		tempData.Save();

		// A confirmação vale para a página que veio do redirect; sobreviver a ela faria o
		// aviso reaparecer na navegação seguinte, sem ação nenhuma por trás.
		provedor.Salvos.Should().NotContainKey(FeedbackViewComponent.ChaveDaMensagem);
	}

	private sealed class ProvedorDeTempData : ITempDataProvider
	{
		public IDictionary<string, object?> Salvos { get; private set; } =
			new Dictionary<string, object?>(StringComparer.Ordinal);

		public IDictionary<string, object?> LoadTempData(HttpContext context) =>
			new Dictionary<string, object?>(StringComparer.Ordinal);

		public void SaveTempData(HttpContext context, IDictionary<string, object?> values) => Salvos = values;
	}
}
