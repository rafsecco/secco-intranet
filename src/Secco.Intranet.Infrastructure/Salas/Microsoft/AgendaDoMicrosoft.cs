using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Secco.Intranet.Application.Salas.Agenda;

namespace Secco.Intranet.Infrastructure.Salas.Microsoft;

/// <summary>
/// Agenda do Microsoft 365 pelo Microsoft Graph, com HTTP puro: os eventos são criados na caixa organizadora
/// e a sala entra como participante do tipo <c>resource</c>. Nunca põe segredo, token nem corpo bruto de
/// resposta no motivo devolvido.
/// </summary>
internal sealed class AgendaDoMicrosoft(HttpClient http, TokenDoMicrosoft token, AgendaOptions opcoes) : IAgendaExterna
{
	private const string Raiz = "https://graph.microsoft.com/v1.0/users/";

	/// <inheritdoc />
	public async Task<ResultadoDaAgenda> CriarAsync(EventoDeAgenda evento, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(evento);

		var resposta = await EnviarAsync(HttpMethod.Post, $"{Raiz}{Caixa()}/events", MontarCorpo(evento, true), ct, false)
			.ConfigureAwait(false);

		if (resposta.Falha is not null)
		{
			return resposta.Falha;
		}

		try
		{
			using var documento = JsonDocument.Parse(resposta.Corpo!);
			var id = documento.RootElement.GetProperty("id").GetString();

			if (!string.IsNullOrEmpty(id))
			{
				return new ResultadoDaAgenda(DesfechoDaAgenda.Feito, id);
			}
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentNullException)
		{
			// cai na falha abaixo
		}

		// A chave de idempotência garante que repetir a criação não duplica o evento.
		return new ResultadoDaAgenda(DesfechoDaAgenda.FalhaPassageira, Motivo: "A agenda não devolveu o identificador do evento.");
	}

	/// <inheritdoc />
	public async Task<ResultadoDaAgenda> AtualizarAsync(string eventoId, EventoDeAgenda evento, CancellationToken ct = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(eventoId);
		ArgumentNullException.ThrowIfNull(evento);

		var resposta = await EnviarAsync(
			HttpMethod.Patch, $"{Raiz}{Caixa()}/events/{Uri.EscapeDataString(eventoId)}", MontarCorpo(evento, false), ct, false)
			.ConfigureAwait(false);

		return resposta.Falha ?? new ResultadoDaAgenda(DesfechoDaAgenda.Feito, eventoId);
	}

	/// <inheritdoc />
	public async Task<ResultadoDaAgenda> ApagarAsync(string eventoId, CancellationToken ct = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(eventoId);

		var resposta = await EnviarAsync(
			HttpMethod.Delete, $"{Raiz}{Caixa()}/events/{Uri.EscapeDataString(eventoId)}", null, ct, true)
			.ConfigureAwait(false);

		return resposta.Falha ?? new ResultadoDaAgenda(DesfechoDaAgenda.Feito);
	}

	private string Caixa() => Uri.EscapeDataString(opcoes.Microsoft.CaixaOrganizadora!.Trim());

	// A transactionId só vale na criação: a documentação diz que não pode ser alterada em atualização.
	private static string MontarCorpo(EventoDeAgenda evento, bool comChave)
	{
		var participantes = new List<object>();

		foreach (var p in evento.Participantes)
		{
			participantes.Add(new { emailAddress = new { address = p.Email, name = p.Nome }, type = "required" });
		}

		if (!string.IsNullOrWhiteSpace(evento.EmailDaSala))
		{
			participantes.Add(new { emailAddress = new { address = evento.EmailDaSala.Trim(), name = evento.Local }, type = "resource" });
		}

		var corpo = new Dictionary<string, object?>
		{
			["subject"] = evento.Assunto,
			["body"] = new { contentType = "text", content = evento.Corpo },
			["start"] = new { dateTime = Utc(evento.Inicio), timeZone = "UTC" },
			["end"] = new { dateTime = Utc(evento.Fim), timeZone = "UTC" },
			["location"] = new { displayName = evento.Local },
			["attendees"] = participantes
		};

		if (comChave)
		{
			corpo["transactionId"] = evento.ChaveDeIdempotencia;
		}

		return JsonSerializer.Serialize(corpo);
	}

	private static string Utc(DateTimeOffset momento) =>
		momento.UtcDateTime.ToString("yyyy-MM-dd\\THH:mm:ss", CultureInfo.InvariantCulture);

	private async Task<RespostaDaAgenda> EnviarAsync(
		HttpMethod verbo, string endereco, string? json, CancellationToken ct, bool naoEncontradoEhFeito)
	{
		for (var tentativa = 0; ; tentativa++)
		{
			var acesso = await token.ObterAsync(ct).ConfigureAwait(false);

			if (acesso.Falha is not null)
			{
				return new RespostaDaAgenda(acesso.Falha, null);
			}

			using var pedido = new HttpRequestMessage(verbo, new Uri(endereco));
			pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acesso.Valor);

			if (json is not null)
			{
				pedido.Content = new StringContent(json, Encoding.UTF8, "application/json");
			}

			HttpResponseMessage resposta;

			try
			{
				resposta = await http.SendAsync(pedido, ct).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (!ct.IsCancellationRequested)
			{
				return Falha(DesfechoDaAgenda.FalhaPassageira, "A agenda não respondeu.");
			}
			catch (HttpRequestException)
			{
				return Falha(DesfechoDaAgenda.FalhaPassageira, "A agenda não pôde ser alcançada.");
			}

			using (resposta)
			{
				var codigo = (int)resposta.StatusCode;

				if (resposta.IsSuccessStatusCode)
				{
					return new RespostaDaAgenda(null, await LerCorpoAsync(resposta, ct).ConfigureAwait(false));
				}

				if (resposta.StatusCode == HttpStatusCode.Unauthorized && tentativa == 0)
				{
					token.Descartar(acesso.Valor);
					continue;
				}

				if (resposta.StatusCode == HttpStatusCode.NotFound && naoEncontradoEhFeito)
				{
					return new RespostaDaAgenda(null, null);
				}

				return Falha(Classificar(codigo), Motivo(codigo));
			}
		}
	}

	private static async Task<string?> LerCorpoAsync(HttpResponseMessage resposta, CancellationToken ct)
	{
		try
		{
			return await resposta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		}
		catch (HttpRequestException)
		{
			return null;
		}
	}

	private static DesfechoDaAgenda Classificar(int codigo) =>
		codigo is 408 or 429 or >= 500 ? DesfechoDaAgenda.FalhaPassageira : DesfechoDaAgenda.FalhaDefinitiva;

	private static string Motivo(int codigo) => codigo switch
	{
		401 => "A agenda recusou as credenciais (401).",
		403 => "A agenda recusou a permissão (403).",
		404 => "A agenda não encontrou a caixa ou o evento (404).",
		400 => "A agenda recusou os dados do evento (400).",
		408 => "A agenda não respondeu a tempo (408).",
		429 => "A agenda pediu para aguardar (429).",
		>= 500 => $"A agenda está indisponível ({codigo}).",
		_ => $"A agenda recusou o pedido ({codigo})."
	};

	private static RespostaDaAgenda Falha(DesfechoDaAgenda desfecho, string motivo) =>
		new(new ResultadoDaAgenda(desfecho, Motivo: motivo), null);

	private sealed record RespostaDaAgenda(ResultadoDaAgenda? Falha, string? Corpo);
}
