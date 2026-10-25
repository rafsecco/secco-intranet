using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Application.Vencimentos;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Web.Models.Vencimentos;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Detalhe e ações dos vencimentos de um setor, em <c>/{setor}/vencimentos/…</c>. A lista mora
/// no <see cref="SetorController"/>, pelo caminho da árvore. Leitura exige
/// <c>setor-{slug}:read</c>, escrita <c>setor-{slug}:write</c>, e recurso desligado ou setor
/// sem acesso respondem 404, sem distinguir um do outro.
/// </summary>
/// <param name="acesso">Checagens de leitura, escrita e recurso ligado.</param>
/// <param name="criarHandler">Cadastro.</param>
/// <param name="editarHandler">Edição.</param>
/// <param name="renovarHandler">Renovação.</param>
/// <param name="encerrarHandler">Encerramento.</param>
/// <param name="reativarHandler">Reativação.</param>
/// <param name="obterHandler">Detalhe.</param>
/// <param name="responsaveisHandler">Quem pode ser responsável.</param>
[Route("{slug:" + SlugDeSetorRouteConstraint.Nome + "}/vencimentos")]
public sealed class VencimentosController(
	AcessoAoRecursoDoSetor acesso,
	CriarVencimentoHandler criarHandler,
	EditarVencimentoHandler editarHandler,
	RenovarVencimentoHandler renovarHandler,
	EncerrarVencimentoHandler encerrarHandler,
	ReativarVencimentoHandler reativarHandler,
	ObterVencimentoHandler obterHandler,
	ListarResponsaveisPossiveisHandler responsaveisHandler) : Controller
{
	private const string MensagemDeAntecedencias = "Informe os dias separados por vírgula, como 30, 7, 1.";

	/// <summary>Detalhe de um vencimento.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do vencimento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}")]
	public async Task<IActionResult> Detalhe(string slug, Guid id, CancellationToken cancellationToken = default)
	{
		if (!await acesso.PodeLerAsync(User, slug, cancellationToken).ConfigureAwait(false))
		{
			return NotFound();
		}

		var alvo = await acesso.ResolverTipoAsync(slug, TipoDeItemMenu.Vencimentos, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var (setor, resolucao) = alvo.Value;
		var detalhe = await obterHandler.HandleAsync(id, setor.Id, cancellationToken).ConfigureAwait(false);

		if (detalhe.IsFailure)
		{
			return NotFound();
		}

		var responsaveis = await responsaveisHandler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);
		var responsavel = responsaveis.FirstOrDefault(r => r.Id == detalhe.Value.ResponsavelId);

		return View(new VencimentoDetalheViewModel(
			setor,
			detalhe.Value,
			responsavel?.Nome,
			ResponsavelSemAcesso: detalhe.Value.ResponsavelId is not null && responsavel is null,
			await acesso.PodeEscreverAsync(User, slug, cancellationToken).ConfigureAwait(false),
			resolucao.No.Nome,
			AcessoAoRecursoDoSetor.Trilho(setor, resolucao)));
	}

	/// <summary>Formulário de cadastro.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("novo")]
	public async Task<IActionResult> Novo(string slug, CancellationToken cancellationToken = default)
	{
		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		return alvo is null
			? NotFound()
			: View("Formulario", await MontarFormularioAsync(alvo.Value, new VencimentoFormViewModel(), null, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Cadastra um vencimento.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("novo")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Novo(string slug, VencimentoFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var (setor, _) = alvo.Value;

		if (!form.TentarLerAntecedencias(out var antecedencias))
		{
			ModelState.AddModelError("Form." + nameof(form.Antecedencias), MensagemDeAntecedencias);
		}

		if (!ModelState.IsValid)
		{
			return View("Formulario", await MontarFormularioAsync(alvo.Value, form, null, cancellationToken).ConfigureAwait(false));
		}

		var criado = await criarHandler.HandleAsync(
			new CriarVencimentoCommand(
				setor.Id, setor.Slug, form.Titulo, form.Descricao, form.DataVencimento!.Value, form.ResponsavelId, antecedencias),
			cancellationToken).ConfigureAwait(false);

		if (criado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, criado.Error.Description);

			return View("Formulario", await MontarFormularioAsync(alvo.Value, form, null, cancellationToken).ConfigureAwait(false));
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Vencimento \"{form.Titulo!.Trim()}\" cadastrado.";

		return RedirectToAction(nameof(Detalhe), new { slug, id = criado.Value });
	}

	/// <summary>Formulário de edição.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do vencimento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}/editar")]
	public async Task<IActionResult> Editar(string slug, Guid id, CancellationToken cancellationToken = default)
	{
		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var detalhe = await obterHandler.HandleAsync(id, alvo.Value.Setor.Id, cancellationToken).ConfigureAwait(false);

		return detalhe.IsFailure
			? NotFound()
			: View("Formulario", await MontarFormularioAsync(alvo.Value, VencimentoFormViewModel.De(detalhe.Value), id, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Grava a edição.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do vencimento.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/editar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Editar(string slug, Guid id, VencimentoFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var (setor, _) = alvo.Value;

		if (!form.TentarLerAntecedencias(out var antecedencias))
		{
			ModelState.AddModelError("Form." + nameof(form.Antecedencias), MensagemDeAntecedencias);
		}

		if (!ModelState.IsValid)
		{
			return View("Formulario", await MontarFormularioAsync(alvo.Value, form, id, cancellationToken).ConfigureAwait(false));
		}

		var editado = await editarHandler.HandleAsync(
			new EditarVencimentoCommand(
				id, setor.Id, setor.Slug, form.Titulo, form.Descricao, form.DataVencimento!.Value, form.ResponsavelId, antecedencias),
			cancellationToken).ConfigureAwait(false);

		if (editado.IsFailure)
		{
			if (editado.Error == IntranetErrors.Vencimentos.NotFound)
			{
				return NotFound();
			}

			ModelState.AddModelError(string.Empty, editado.Error.Description);

			return View("Formulario", await MontarFormularioAsync(alvo.Value, form, id, cancellationToken).ConfigureAwait(false));
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Vencimento \"{form.Titulo!.Trim()}\" atualizado.";

		return RedirectToAction(nameof(Detalhe), new { slug, id });
	}

	/// <summary>Renova o vencimento para uma data nova.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do vencimento.</param>
	/// <param name="novaData">Data nova.</param>
	/// <param name="observacao">Observação opcional para o histórico.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/renovar")]
	[ValidateAntiForgeryToken]
	public Task<IActionResult> Renovar(
		string slug, Guid id, DateOnly? novaData, string? observacao, CancellationToken cancellationToken = default) =>
		AcaoAsync(
			slug,
			id,
			novaData,
			(setor, data) => renovarHandler.HandleAsync(
				new RenovarVencimentoCommand(id, setor.Id, setor.Slug, data, observacao), cancellationToken),
			data => $"Vencimento renovado para {data:dd/MM/yyyy}.",
			cancellationToken);

	/// <summary>Encerra o vencimento: os avisos param.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do vencimento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/encerrar")]
	[ValidateAntiForgeryToken]
	public Task<IActionResult> Encerrar(string slug, Guid id, CancellationToken cancellationToken = default) =>
		AcaoAsync<bool>(
			slug,
			id,
			true,
			(setor, _) => encerrarHandler.HandleAsync(new EncerrarVencimentoCommand(id, setor.Id, setor.Slug), cancellationToken),
			_ => "Vencimento encerrado. Os avisos pararam.",
			cancellationToken);

	/// <summary>Reativa um vencimento encerrado, com uma data nova.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do vencimento.</param>
	/// <param name="novaData">Data nova.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/reativar")]
	[ValidateAntiForgeryToken]
	public Task<IActionResult> Reativar(string slug, Guid id, DateOnly? novaData, CancellationToken cancellationToken = default) =>
		AcaoAsync(
			slug,
			id,
			novaData,
			(setor, data) => reativarHandler.HandleAsync(
				new ReativarVencimentoCommand(id, setor.Id, setor.Slug, data), cancellationToken),
			data => $"Vencimento reativado até {data:dd/MM/yyyy}.",
			cancellationToken);

	/// <summary>
	/// Passo comum das ações do detalhe: confere a escrita, executa o caso de uso e volta ao
	/// detalhe com o feedback, de sucesso ou da recusa do caso de uso.
	/// </summary>
	private async Task<IActionResult> AcaoAsync<TDado>(
		string slug,
		Guid id,
		TDado? dado,
		Func<SetorDto, TDado, Task<Result<Guid>>> executar,
		Func<TDado, string> mensagem,
		CancellationToken cancellationToken)
		where TDado : struct
	{
		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		if (dado is null)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = "Informe a data.";

			return RedirectToAction(nameof(Detalhe), new { slug, id });
		}

		var resultado = await executar(alvo.Value.Setor, dado.Value).ConfigureAwait(false);

		if (resultado.IsFailure && resultado.Error == IntranetErrors.Vencimentos.NotFound)
		{
			return NotFound();
		}

		if (resultado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}
		else
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = mensagem(dado.Value);
		}

		return RedirectToAction(nameof(Detalhe), new { slug, id });
	}

	/// <summary>Escrita: permissão do setor e recurso ligado; nulo vira 404 em quem chama.</summary>
	private async Task<(SetorDto Setor, ResultadoDaResolucao Resolucao)?> AlvoDeEscritaAsync(
		string slug, CancellationToken cancellationToken)
	{
		if (!await acesso.PodeEscreverAsync(User, slug, cancellationToken).ConfigureAwait(false))
		{
			return null;
		}

		return await acesso.ResolverTipoAsync(slug, TipoDeItemMenu.Vencimentos, cancellationToken).ConfigureAwait(false);
	}

	private async Task<VencimentoFormPaginaViewModel> MontarFormularioAsync(
		(SetorDto Setor, ResultadoDaResolucao Resolucao) alvo,
		VencimentoFormViewModel form,
		Guid? id,
		CancellationToken cancellationToken)
	{
		var responsaveis = await responsaveisHandler.HandleAsync(alvo.Setor.Slug, cancellationToken).ConfigureAwait(false);

		return new VencimentoFormPaginaViewModel(
			alvo.Setor,
			form,
			responsaveis,
			id,
			alvo.Resolucao.No.Nome,
			AcessoAoRecursoDoSetor.Trilho(alvo.Setor, alvo.Resolucao),
			string.Join('/', alvo.Resolucao.CaminhoCompleto));
	}
}
