using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Acesso;
using Secco.Intranet.Web.Models.Diretorio;
using Secco.Intranet.Web.Navigation;
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
[Route("diretorio")]
[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Usuario)]
public sealed class DiretorioController(
	ListarPessoasHandler listarPessoas,
	ObterPessoaHandler obterPessoa) : Controller
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

		var ehOProprio = AcessoAoDiretorio.UsuarioId(User) == id;
		var ehAdmin = AcessoAoDiretorio.TemNivel(User, NivelDeAcessoAoDiretorio.Administrador);
		var editarUrl = ehAdmin ? $"/diretorio/{id}/editar" : ehOProprio ? "/diretorio/perfil" : null;

		return View(new PessoaViewModel(resultado.Value, editarUrl is not null, editarUrl));
	}

	/// <summary>"Meu perfil": por ora leva à própria página; a etapa de edição o transforma no formulário.</summary>
	[HttpGet("perfil")]
	public IActionResult Perfil()
	{
		var id = AcessoAoDiretorio.UsuarioId(User);

		return id is null ? View("SemUsuario") : RedirectToAction(nameof(Pessoa), new { id });
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
