using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Documentos;
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
/// <param name="importar">Importação CSV.</param>
/// <param name="fotoDoColaborador">Envio, remoção e consulta da foto.</param>
/// <param name="arquivos">Armazenamento cifrado, de onde a foto sai.</param>
/// <param name="authorizationService">Serviço de autorização do framework (ADR-0021).</param>
/// <param name="logger">Log.</param>
[Route("diretorio")]
[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Usuario)]
public sealed class DiretorioController(
	ListarPessoasHandler listarPessoas,
	ObterPessoaHandler obterPessoa,
	ObterPessoaParaEdicaoHandler obterPessoaParaEdicao,
	EditarContatoHandler editarContato,
	EditarDadosFuncionaisHandler editarDadosFuncionais,
	MontarOrganogramaHandler montarOrganograma,
	ImportarDiretorioHandler importar,
	FotoDoColaboradorHandler fotoDoColaborador,
	IArquivoStore arquivos,
	IAuthorizationService authorizationService,
	ILogger<DiretorioController> logger) : Controller
{
	private const long LimiteDeBytesDoArquivo = 2 * 1024 * 1024;

	// 10 MB da foto mais folga para o restante do multipart (campos de recorte e token).
	private const long LimiteDaRequisicaoDaFoto = 10 * 1024 * 1024 + 64 * 1024;

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
			: View(new DiretorioViewModel(
				resultado.Value, busca, setor,
				await AcessoAoDiretorio.TemNivelAsync(authorizationService, User, NivelDeAcessoAoDiretorio.Administrador)));
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

		if (await AcessoAoDiretorio.TemNivelAsync(authorizationService, User, NivelDeAcessoAoDiretorio.Administrador))
		{
			editarUrl = $"/diretorio/{id}/editar";
		}
		else if (AcessoAoDiretorio.UsuarioId(User) == id)
		{
			editarUrl = "/diretorio/perfil";
		}

		return View(new PessoaViewModel(resultado.Value, editarUrl is not null, editarUrl));
	}

	/// <summary>Formulário de importação CSV.</summary>
	[HttpGet("importar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	public IActionResult Importar() => View();

	/// <summary>Lê o arquivo e mostra o que aconteceria. <b>Não grava.</b></summary>
	/// <param name="arquivo">CSV enviado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("importar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Importar(IFormFile? arquivo, CancellationToken cancellationToken = default)
	{
		if (arquivo is null || arquivo.Length == 0)
		{
			ModelState.AddModelError(string.Empty, "Escolha um arquivo CSV para importar.");

			return View();
		}

		if (arquivo.Length > LimiteDeBytesDoArquivo)
		{
			ModelState.AddModelError(string.Empty, "O arquivo passa do limite de 1 MB.");

			return View();
		}

		string texto;

		using (var leitor = new StreamReader(arquivo.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
		{
			texto = await leitor.ReadToEndAsync(cancellationToken);
		}

		var relatorio = await importar.PrevisualizarAsync(new ImportarDiretorioCommand(texto), cancellationToken);

		if (relatorio.IsFailure)
		{
			ModelState.AddModelError(string.Empty, relatorio.Error.Description);

			return View();
		}

		return View("ImportarRelatorio", new ImportacaoViewModel(relatorio.Value, texto));
	}

	/// <summary>
	/// Aplica a importação. Revalida o CSV inteiro — nunca confia na pré-visualização, que passou
	/// pelo navegador — e grava as linhas válidas.
	/// </summary>
	/// <param name="csv">Texto do arquivo, devolvido pelo formulário de confirmação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("importar/aplicar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	[ValidateAntiForgeryToken]
	[RequestFormLimits(ValueLengthLimit = 8 * 1024 * 1024)]
	public async Task<IActionResult> Aplicar(string? csv, CancellationToken cancellationToken = default)
	{
		var relatorio = await importar.AplicarAsync(new ImportarDiretorioCommand(csv ?? string.Empty), cancellationToken);

		if (relatorio.IsFailure)
		{
			ModelState.AddModelError(string.Empty, relatorio.Error.Description);

			return View(nameof(Importar));
		}

		return View("ImportarRelatorio", new ImportacaoViewModel(relatorio.Value, string.Empty));
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

		return View(new MeuPerfilViewModel(pessoa, new EditarContatoForm { Nome = pessoa.NomeDeExibicao, Ramal = pessoa.Ramal, Sobre = pessoa.Sobre }));
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

	/// <summary>
	/// Troca a foto de quem está logado. O id vem <b>sempre</b> do claim <c>sub</c>; esta rota não
	/// recebe id algum, então um id forjado no formulário não tem onde entrar.
	/// </summary>
	/// <param name="foto">Imagem enviada.</param>
	/// <param name="x">Esquerda do recorte, em pixels da imagem original.</param>
	/// <param name="y">Topo do recorte.</param>
	/// <param name="largura">Largura do recorte.</param>
	/// <param name="altura">Altura do recorte.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("perfil/foto")]
	[ValidateAntiForgeryToken]
	[RequestSizeLimit(LimiteDaRequisicaoDaFoto)]
	public async Task<IActionResult> EnviarFotoPropria(IFormFile? foto, int? x, int? y, int? largura, int? altura, CancellationToken cancellationToken = default)
	{
		var id = AcessoAoDiretorio.UsuarioId(User);

		if (id is null)
		{
			return View("SemUsuario");
		}

		await DefinirFotoAsync(id.Value, foto, x, y, largura, altura, cancellationToken);

		return RedirectToAction(nameof(Perfil));
	}

	/// <summary>Tira a foto de quem está logado (volta às iniciais).</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("perfil/foto/remover")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> RemoverFotoPropria(CancellationToken cancellationToken = default)
	{
		var id = AcessoAoDiretorio.UsuarioId(User);

		if (id is null)
		{
			return View("SemUsuario");
		}

		await RemoverFotoAsync(id.Value, cancellationToken);

		return RedirectToAction(nameof(Perfil));
	}

	/// <summary>Troca a foto de outra pessoa (admin do diretório).</summary>
	/// <param name="id">Pessoa dona da foto.</param>
	/// <param name="foto">Imagem enviada.</param>
	/// <param name="x">Esquerda do recorte, em pixels da imagem original.</param>
	/// <param name="y">Topo do recorte.</param>
	/// <param name="largura">Largura do recorte.</param>
	/// <param name="altura">Altura do recorte.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/foto")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	[ValidateAntiForgeryToken]
	[RequestSizeLimit(LimiteDaRequisicaoDaFoto)]
	public async Task<IActionResult> EnviarFoto(Guid id, IFormFile? foto, int? x, int? y, int? largura, int? altura, CancellationToken cancellationToken = default)
	{
		var existe = await obterPessoaParaEdicao.HandleAsync(id, cancellationToken);

		if (existe.IsFailure)
		{
			return Falha(existe.Error);
		}

		await DefinirFotoAsync(id, foto, x, y, largura, altura, cancellationToken);

		return RedirectToAction(nameof(Editar), new { id });
	}

	/// <summary>Tira a foto de outra pessoa (admin do diretório).</summary>
	/// <param name="id">Pessoa dona da foto.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/foto/remover")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> RemoverFoto(Guid id, CancellationToken cancellationToken = default)
	{
		var existe = await obterPessoaParaEdicao.HandleAsync(id, cancellationToken);

		if (existe.IsFailure)
		{
			return Falha(existe.Error);
		}

		await RemoverFotoAsync(id, cancellationToken);

		return RedirectToAction(nameof(Editar), new { id });
	}

	/// <summary>
	/// Entrega a foto decifrada a qualquer pessoa com acesso ao Diretório (o filtro da classe).
	/// <c>no-cache</c> + <c>ETag</c>: o navegador sempre revalida e leva 304 enquanto a foto não muda;
	/// <c>private</c> impede cache compartilhado de um conteúdo autenticado.
	/// </summary>
	/// <param name="id">Pessoa dona da foto.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}/foto")]
	public async Task<IActionResult> Foto(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await fotoDoColaborador.ObterAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return resultado.Error.Type == ErrorType.NotFound ? NotFound() : StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		var foto = resultado.Value;

		Response.Headers.ETag = foto.ETag;
		Response.Headers.CacheControl = "private, no-cache";
		Response.Headers.XContentTypeOptions = "nosniff";

		if (Request.Headers.IfNoneMatch.Any(valor => ETagConfere(valor, foto.ETag)))
		{
			return StatusCode(StatusCodes.Status304NotModified);
		}

		// Decifra antes de escrever o primeiro byte: arquivo ausente ou corrompido vira 404, não 500 com
		// cabeçalhos pela metade. A foto tem no máximo 256x256 em WebP (poucos KB), então cabe em memória.
		using var conteudo = new MemoryStream();

		try
		{
			await arquivos.EscreverEmAsync(foto.CaminhoRelativo, foto.ChaveEmbrulhada, conteudo, cancellationToken);
		}
		catch (Exception falha) when (falha is not OperationCanceledException)
		{
			// Só o tipo da exceção: a mensagem de falhas de I/O costuma trazer o caminho absoluto.
			logger.LogWarning("Não foi possível ler a foto de {UsuarioId} do armazenamento ({TipoDaFalha}).", id, falha.GetType().Name);

			Response.Headers.Remove("ETag");

			return NotFound();
		}

		Response.ContentType = foto.ContentType;
		conteudo.Position = 0;
		await conteudo.CopyToAsync(Response.Body, cancellationToken);

		return new EmptyResult();
	}

	// If-None-Match pode trazer uma lista de validadores, fracos (W/) ou "*".
	private static bool ETagConfere(string? cabecalho, string etag) =>
		cabecalho is not null
		&& cabecalho.Split(',').Select(parte => parte.Trim()).Any(parte => parte == "*" || (parte.StartsWith("W/", StringComparison.Ordinal) ? parte[2..] : parte) == etag);

	private async Task DefinirFotoAsync(Guid usuarioId, IFormFile? foto, int? x, int? y, int? largura, int? altura, CancellationToken cancellationToken)
	{
		if (foto is null || foto.Length == 0)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = "Escolha uma foto.";

			return;
		}

		// Coordenadas incompletas: sem recorte, vale o quadrado central.
		var recorte = x is { } e && y is { } t && largura is { } l && altura is { } a ? new RecorteDaFoto(e, t, l, a) : null;

		await using var upload = foto.OpenReadStream();
		var resultado = await fotoDoColaborador.DefinirAsync(usuarioId, upload, recorte, cancellationToken);

		TempData[FeedbackViewComponent.ChaveDaMensagem] = resultado.IsSuccess ? "Foto atualizada." : resultado.Error.Description;
	}

	private async Task RemoverFotoAsync(Guid usuarioId, CancellationToken cancellationToken)
	{
		var resultado = await fotoDoColaborador.RemoverAsync(usuarioId, cancellationToken);

		TempData[FeedbackViewComponent.ChaveDaMensagem] = resultado.IsSuccess ? "Foto removida." : resultado.Error.Description;
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
				Nome = pessoa.NomeDeExibicao,
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
