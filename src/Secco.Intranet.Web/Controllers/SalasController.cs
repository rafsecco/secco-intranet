using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Salas;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Salas de reunião e suas reservas. Controller fino (ADR-0002): só orquestra os handlers. O cadastro
/// das salas (<c>gerenciar</c>) é exclusivo do <c>intranet-admin</c> (ADR-0008).
/// </summary>
/// <param name="criarSala">Caso de uso de criação de sala.</param>
/// <param name="editarSala">Caso de uso de edição de sala.</param>
/// <param name="excluirSala">Caso de uso de exclusão de sala.</param>
/// <param name="listarSalas">Listagem das salas.</param>
/// <param name="obterSala">Leitura pontual de sala.</param>
/// <param name="reservarSala">Caso de uso de reserva.</param>
/// <param name="alterarReserva">Caso de uso de alteração de reserva.</param>
/// <param name="cancelarReserva">Caso de uso de cancelamento de reserva.</param>
/// <param name="obterReserva">Leitura pontual de reserva.</param>
/// <param name="listarOcupacao">Ocupação das salas num dia.</param>
/// <param name="listarMinhas">Reservas futuras da pessoa.</param>
/// <param name="listarConvidados">Pessoas que podem ser convidadas.</param>
/// <param name="relogio">Relógio local das reservas.</param>
/// <param name="environment">Ambiente de hospedagem — o modo aberto só vale em Development (ADR-0020).</param>
/// <param name="configuration">Configuração do host, para saber se a autenticação está ativa.</param>
[Route("salas")]
public sealed class SalasController(
	CriarSalaHandler criarSala,
	EditarSalaHandler editarSala,
	ExcluirSalaHandler excluirSala,
	ListarSalasHandler listarSalas,
	ObterSalaHandler obterSala,
	ReservarSalaHandler reservarSala,
	AlterarReservaHandler alterarReserva,
	CancelarReservaHandler cancelarReserva,
	ObterReservaHandler obterReserva,
	ListarOcupacaoDoDiaHandler listarOcupacao,
	ListarMinhasReservasHandler listarMinhas,
	ListarConvidadosPossiveisHandler listarConvidados,
	RelogioDeSalas relogio,
	IWebHostEnvironment environment,
	IConfiguration configuration) : Controller
{
	// Id de quem "está logado" no modo aberto de desenvolvimento, em que não há usuário autenticado.
	private static readonly Guid UsuarioDeDesenvolvimento = Guid.Parse("00000000-0000-0000-0000-00000000d0e5");

	/// <summary>Grade do dia: uma coluna por sala ativa.</summary>
	/// <param name="data">Dia; inválido ou ausente cai em hoje.</param>
	/// <param name="sala">Sala em foco nas telas estreitas.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("")]
	public async Task<IActionResult> Index(DateOnly? data, Guid? sala, CancellationToken cancellationToken = default)
	{
		if (QuemPede() is not { } quem)
		{
			return Challenge();
		}

		var hoje = relogio.Hoje();
		var ocupacao = await listarOcupacao.HandleAsync(data ?? hoje, quem, cancellationToken);
		var agora = relogio.ParaLocal(relogio.Agora()).Hora;
		var grade = GradeDoDiaViewModel.Montar(ocupacao, hoje, agora, sala);
		grade.PodeGerenciarSalas = quem.EhAdministrador;

		return View(grade);
	}

	/// <summary>Formulário de reserva nova.</summary>
	/// <param name="sala">Sala pré-escolhida.</param>
	/// <param name="data">Dia pré-escolhido.</param>
	/// <param name="inicio">Hora de início pré-escolhida.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("reservar")]
	public async Task<IActionResult> Reservar(Guid? sala, DateOnly? data, TimeOnly? inicio, CancellationToken cancellationToken = default)
	{
		if (QuemPede() is not { } quem)
		{
			return Challenge();
		}

		var comeco = inicio ?? new TimeOnly(9, 0);
		var form = new ReservaFormViewModel
		{
			SalaId = sala,
			Data = data ?? relogio.Hoje(),
			Inicio = comeco,
			Fim = comeco.Hour == 23 ? null : comeco.AddHours(1),
		};

		return await FormularioAsync(form, quem, cancellationToken);
	}

	/// <summary>Processa uma reserva nova.</summary>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("reservar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Reservar(ReservaFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (QuemPede() is not { } quem)
		{
			return Challenge();
		}

		if (!ModelState.IsValid)
		{
			return await FormularioAsync(form, quem, cancellationToken);
		}

		var resultado = await reservarSala.HandleAsync(
			new ReservarSalaCommand(
				form.SalaId!.Value, form.Data!.Value, form.Inicio!.Value, form.Fim!.Value, form.Titulo, form.Descricao,
				[.. form.Convidados], quem),
			cancellationToken);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return await FormularioAsync(form, quem, cancellationToken);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = "Reserva criada.";

		return VoltarParaGrade(form);
	}

	/// <summary>Detalhe de uma reserva; qualquer pessoa autenticada a enxerga.</summary>
	/// <param name="id">Identificador da reserva.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("reservas/{id:guid}")]
	public async Task<IActionResult> Reserva(Guid id, CancellationToken cancellationToken = default)
	{
		if (QuemPede() is not { } quem)
		{
			return Challenge();
		}

		var resultado = await obterReserva.HandleAsync(id, quem, cancellationToken);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		var reserva = resultado.Value;
		var ehDono = reserva.ReservadoPor.UsuarioId == quem.UsuarioId;

		return View(new ReservaDetalheViewModel(reserva, ehDono || quem.EhAdministrador));
	}

	/// <summary>Formulário de alteração de uma reserva.</summary>
	/// <param name="id">Identificador da reserva.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("reservas/{id:guid}/alterar")]
	public async Task<IActionResult> Alterar(Guid id, CancellationToken cancellationToken = default)
	{
		if (QuemPede() is not { } quem)
		{
			return Challenge();
		}

		var resultado = await obterReserva.HandleAsync(id, quem, cancellationToken);

		if (resultado.IsFailure || !resultado.Value.PodeAlterar)
		{
			return NotFound();
		}

		var reserva = resultado.Value;
		var form = new ReservaFormViewModel
		{
			Id = reserva.Id,
			SalaId = reserva.SalaId,
			Data = reserva.Dia,
			Inicio = reserva.Inicio,
			Fim = reserva.Fim,
			Titulo = reserva.Titulo,
			Descricao = reserva.Descricao,
			Convidados = [.. reserva.Convidados.Select(convidado => convidado.UsuarioId)],
		};

		return await FormularioAsync(form, quem, cancellationToken);
	}

	/// <summary>Processa a alteração de uma reserva.</summary>
	/// <param name="id">Identificador da reserva.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("reservas/{id:guid}/alterar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Alterar(Guid id, ReservaFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (QuemPede() is not { } quem)
		{
			return Challenge();
		}

		form.Id = id;

		if (!ModelState.IsValid)
		{
			return await FormularioAsync(form, quem, cancellationToken);
		}

		var resultado = await alterarReserva.HandleAsync(
			new AlterarReservaCommand(
				id, form.SalaId!.Value, form.Data!.Value, form.Inicio!.Value, form.Fim!.Value, form.Titulo, form.Descricao,
				[.. form.Convidados], quem),
			cancellationToken);

		if (resultado.IsFailure)
		{
			if (resultado.Error == IntranetErrors.Salas.ReservaNotFound)
			{
				return NotFound();
			}

			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return await FormularioAsync(form, quem, cancellationToken);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = "Reserva alterada.";

		return VoltarParaGrade(form);
	}

	/// <summary>Cancela uma reserva.</summary>
	/// <param name="id">Identificador da reserva.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("reservas/{id:guid}/cancelar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Cancelar(Guid id, CancellationToken cancellationToken = default)
	{
		if (QuemPede() is not { } quem)
		{
			return Challenge();
		}

		var antes = await obterReserva.HandleAsync(id, quem, cancellationToken);
		var resultado = await cancelarReserva.HandleAsync(id, quem, cancellationToken);

		if (resultado.IsFailure)
		{
			if (resultado.Error.Type == ErrorType.NotFound)
			{
				return NotFound();
			}

			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;

			return RedirectToAction(nameof(Reserva), new { id });
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = "Reserva cancelada.";

		return antes.IsSuccess
			? RedirectToAction(nameof(Index), new { data = Texto(antes.Value.Dia), sala = antes.Value.SalaId })
			: RedirectToAction(nameof(Index));
	}

	/// <summary>Reservas futuras em que a pessoa é dona ou convidada.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("minhas")]
	public async Task<IActionResult> Minhas(CancellationToken cancellationToken = default)
	{
		if (QuemPede() is not { } quem)
		{
			return Challenge();
		}

		return View(await listarMinhas.HandleAsync(quem, cancellationToken));
	}
	/// <summary>Lista todas as salas, ativas e inativas.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("gerenciar")]
	[SomenteIntranetAdmin]
	public async Task<IActionResult> Gerenciar(CancellationToken cancellationToken = default) =>
		View(await listarSalas.HandleAsync(somenteAtivas: false, cancellationToken));

	/// <summary>Formulário de sala nova.</summary>
	[HttpGet("gerenciar/nova")]
	[SomenteIntranetAdmin]
	public IActionResult Nova() => View("SalaForm", new SalaFormViewModel());

	/// <summary>Processa a criação de uma sala.</summary>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("gerenciar/nova")]
	[SomenteIntranetAdmin]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Nova(SalaFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (!ModelState.IsValid)
		{
			return View("SalaForm", form);
		}

		var resultado = await criarSala.HandleAsync(Comando(form, null), cancellationToken);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View("SalaForm", form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Sala \"{form.Nome!.Trim()}\" criada.";

		return RedirectToAction(nameof(Gerenciar));
	}

	/// <summary>Formulário de edição de sala.</summary>
	/// <param name="id">Identificador da sala.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("gerenciar/{id:guid}/editar")]
	[SomenteIntranetAdmin]
	public async Task<IActionResult> Editar(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await obterSala.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		var sala = resultado.Value;

		return View("SalaForm", new SalaFormViewModel
		{
			Id = sala.Id,
			Nome = sala.Nome,
			Localizacao = sala.Localizacao,
			Capacidade = sala.Capacidade,
			Recursos = sala.Recursos,
			EmailNaAgenda = sala.EmailNaAgenda,
			Ordem = sala.Ordem,
			Ativa = sala.Ativa,
		});
	}

	/// <summary>Processa a edição de uma sala.</summary>
	/// <param name="id">Identificador da sala.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("gerenciar/{id:guid}/editar")]
	[SomenteIntranetAdmin]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Editar(Guid id, SalaFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		form.Id = id;

		if (!ModelState.IsValid)
		{
			return View("SalaForm", form);
		}

		var resultado = await editarSala.HandleAsync(Comando(form, id), cancellationToken);

		if (resultado.IsFailure)
		{
			if (resultado.Error.Type == ErrorType.NotFound)
			{
				return NotFound();
			}

			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View("SalaForm", form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Sala \"{form.Nome!.Trim()}\" salva.";

		return RedirectToAction(nameof(Gerenciar));
	}

	/// <summary>Exclui uma sala sem reservas futuras.</summary>
	/// <param name="id">Identificador da sala.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("gerenciar/{id:guid}/excluir")]
	[SomenteIntranetAdmin]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await excluirSala.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}
		else
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = "Sala excluída.";
		}

		return RedirectToAction(nameof(Gerenciar));
	}

	/// <summary>
	/// Quem pede: o usuário autenticado (administrador se for <c>intranet-admin</c>) ou, no modo aberto de
	/// desenvolvimento sem usuário, um id fixo com poderes de administrador. <c>null</c> pede login.
	/// </summary>
	private QuemPede? QuemPede()
	{
		var usuarioId = AcessoAoDiretorio.UsuarioId(User);

		if (usuarioId is not null)
		{
			return new QuemPede(usuarioId.Value, AcessoAdministrativo.SomenteIntranetAdmin(User));
		}

		return AcessoAdministrativo.ModoAbertoDeDev(environment, configuration)
			? new QuemPede(UsuarioDeDesenvolvimento, true)
			: null;
	}

	private async Task<IActionResult> FormularioAsync(ReservaFormViewModel form, QuemPede quem, CancellationToken cancellationToken)
	{
		form.Salas = await listarSalas.HandleAsync(somenteAtivas: true, cancellationToken);
		form.Pessoas = await listarConvidados.HandleAsync(quem.UsuarioId, cancellationToken);

		if (form.SalaId is null && form.Salas.Count > 0)
		{
			form.SalaId = form.Salas[0].Id;
		}

		return View("ReservaForm", form);
	}

	private RedirectToActionResult VoltarParaGrade(ReservaFormViewModel form) =>
		RedirectToAction(nameof(Index), new { data = Texto(form.Data!.Value), sala = form.SalaId });

	private static string Texto(DateOnly dia) => dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	private static SalvarSalaCommand Comando(SalaFormViewModel form, Guid? id) =>
		new(id, form.Nome, form.Localizacao, form.Capacidade, form.Recursos, form.EmailNaAgenda, form.Ordem, form.Ativa);
}
