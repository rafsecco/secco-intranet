using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Acesso;
using Secco.Intranet.Web.Models.Diretorio;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Diretório organizacional: pessoas, perfil e (nas próximas etapas) organograma e importação.
/// Toda rota exige nível <c>Usuario</c> (<c>diretorio-user</c>, <c>diretorio-admin</c> ou
/// <c>intranet-admin</c>); quem não tem recebe 403 — não só o link escondido no menu.
/// Controller fino (ADR-0002): só orquestra handlers.
/// </summary>
/// <param name="listarPessoas">Grade de pessoas.</param>
/// <param name="obterPessoa">Detalhe de uma pessoa.</param>
/// <param name="obterPessoaParaEdicao">Dados do formulário de edição completa.</param>
/// <param name="editarContato">Edição do contato.</param>
/// <param name="editarDadosFuncionais">Edição de cargo, setor e gestor.</param>
/// <param name="montarOrganograma">Organograma por gestor.</param>
[Route("diretorio")]
[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Usuario)]
public sealed class DiretorioController(
	ListarPessoasHandler listarPessoas,
	ObterPessoaHandler obterPessoa,
	ObterPessoaParaEdicaoHandler obterPessoaParaEdicao,
	EditarContatoHandler editarContato,
	EditarDadosFuncionaisHandler editarDadosFuncionais,
	MontarOrganogramaHandler montarOrganograma) : Controller
{
	/// <summary>Grade de pessoas com busca e filtro por setor.</summary>
	/// <param name="busca">Trecho de nome, cargo, setor ou e-mail.</param>
	/// <param name="setor">Slug do setor de lotação.</param>
	/// <param name="page">Página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("")]
	public async Task<IActionResult> Index(string? busca, string? setor, int page = 1, CancellationToken cancellationToken = default)
	{
		var resultado = await listarPessoas.HandleAsync(new ListarPessoasQuery(busca, setor, page), cancellationToken);

		return resultado.IsFailure
			? Falha(resultado.Error)
			: View(new DiretorioViewModel(resultado.Value, busca, setor));
	}

	/// <summary>Perfil de uma pessoa.</summary>
	/// <param name="id">Id da pessoa no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}")]
	public async Task<IActionResult> Pessoa(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await obterPessoa.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return Falha(resultado.Error);
		}

		string? editarUrl = null;

		if (AcessoAoDiretorio.TemNivel(User, NivelDeAcessoAoDiretorio.Administrador))
		{
			editarUrl = $"/diretorio/{id}/editar";
		}
		else if (AcessoAoDiretorio.UsuarioId(User) == id)
		{
			editarUrl = "/diretorio/perfil";
		}

		return View(new PessoaViewModel(resultado.Value, editarUrl is not null, editarUrl));
	}

	/// <summary>Organograma: árvore por gestor.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("organograma")]
	public async Task<IActionResult> Organograma(CancellationToken cancellationToken = default)
	{
		var resultado = await montarOrganograma.HandleAsync(cancellationToken);

		return resultado.IsFailure ? Falha(resultado.Error) : View(new OrganogramaViewModel(resultado.Value));
	}

	/// <summary>"Meu perfil": o formulário de contato de quem está logado.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("perfil")]
	public async Task<IActionResult> Perfil(CancellationToken cancellationToken = default)
	{
		var id = AcessoAoDiretorio.UsuarioId(User);

		if (id is null)
		{
			return View("SemUsuario");
		}

		var resultado = await obterPessoa.HandleAsync(id.Value, cancellationToken);

		if (resultado.IsFailure)
		{
			return Falha(resultado.Error);
		}

		var pessoa = resultado.Value.Pessoa;

		return View(new MeuPerfilViewModel(pessoa, new EditarContatoForm { Nome = pessoa.TemPerfil ? pessoa.Nome : null, Ramal = pessoa.Ramal, Sobre = pessoa.Sobre }));
	}

	/// <summary>
	/// Salva o contato de quem está logado. O id vem <b>sempre</b> do claim <c>sub</c>, nunca do
	/// formulário, e o modelo só declara contato: cargo, setor e gestor forjados não têm onde entrar.
	/// </summary>
	/// <param name="form">Contato.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("perfil")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Perfil(EditarContatoForm form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var id = AcessoAoDiretorio.UsuarioId(User);

		if (id is null)
		{
			return View("SemUsuario");
		}

		if (ModelState.IsValid)
		{
			var salvo = await editarContato.HandleAsync(new EditarContatoCommand(id.Value, form.Nome, form.Ramal, form.Sobre), cancellationToken);

			if (salvo.IsSuccess)
			{
				TempData[FeedbackViewComponent.ChaveDaMensagem] = "Perfil atualizado.";

				return RedirectToAction(nameof(Perfil));
			}

			ModelState.AddModelError(string.Empty, salvo.Error.Description);
		}

		var atual = await obterPessoa.HandleAsync(id.Value, cancellationToken);

		return atual.IsFailure ? Falha(atual.Error) : View(new MeuPerfilViewModel(atual.Value.Pessoa, form));
	}

	/// <summary>Edição completa de uma pessoa pelo admin do diretório.</summary>
	/// <param name="id">Pessoa a editar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}/editar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	public async Task<IActionResult> Editar(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await obterPessoaParaEdicao.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return Falha(resultado.Error);
		}

		var pessoa = resultado.Value.Pessoa;

		return View(new EditarPessoaViewModel(
			resultado.Value,
			new EditarPessoaForm
			{
				Nome = pessoa.TemPerfil ? pessoa.Nome : null,
				Ramal = pessoa.Ramal,
				Sobre = pessoa.Sobre,
				Cargo = pessoa.Cargo,
				SetorId = pessoa.SetorId,
				GestorUsuarioId = pessoa.GestorUsuarioId,
			}));
	}

	/// <summary>Salva a edição completa. Dados funcionais primeiro (têm as regras); só se passarem, o contato.</summary>
	/// <param name="id">Pessoa a editar.</param>
	/// <param name="form">Contato e dados funcionais.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/editar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Editar(Guid id, EditarPessoaForm form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (ModelState.IsValid)
		{
			var funcionais = await editarDadosFuncionais.HandleAsync(
				new EditarDadosFuncionaisCommand(id, form.Cargo, form.SetorId, form.GestorUsuarioId), cancellationToken);

			var resultado = funcionais.IsFailure
				? funcionais
				: await editarContato.HandleAsync(new EditarContatoCommand(id, form.Nome, form.Ramal, form.Sobre), cancellationToken);

			if (resultado.IsSuccess)
			{
				TempData[FeedbackViewComponent.ChaveDaMensagem] = "Dados atualizados.";

				return RedirectToAction(nameof(Pessoa), new { id });
			}

			// A resposta é a própria página (sem redirect), então o erro vai no ModelState e a view o
			// mostra no resumo de validação; TempData só apareceria no clique seguinte.
			ModelState.AddModelError(string.Empty, resultado.Error.Description);
		}

		var dados = await obterPessoaParaEdicao.HandleAsync(id, cancellationToken);

		return dados.IsFailure ? Falha(dados.Error) : View(new EditarPessoaViewModel(dados.Value, form));
	}

	/// <summary>
	/// Erro de leitura: pessoa inexistente é 404; o resto (SecureGate ausente ou fora do ar) vira a
	/// página que explica, com 200 — não é falha da Intranet.
	/// </summary>
	private IActionResult Falha(Error erro) =>
		erro.Type == ErrorType.NotFound
			? NotFound()
			: View("Indisponivel", new IndisponivelViewModel(erro.Description));
}
