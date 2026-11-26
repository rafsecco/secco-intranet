using System.Text.Json;
using Secco.Intranet.Application.Salas.Agenda;

namespace Secco.Intranet.Infrastructure.Salas.Microsoft;

/// <summary>Resultado do pedido de token: o token, ou a falha já traduzida (nunca com segredo nem corpo bruto).</summary>
/// <param name="Valor">Token de acesso, quando o pedido deu certo.</param>
/// <param name="Falha">Falha da agenda, quando o pedido não deu certo.</param>
internal sealed record ResultadoDoToken(string? Valor, ResultadoDaAgenda? Falha);

/// <summary>
/// Token de aplicativo do Microsoft 365 (credenciais de cliente), guardado em memória até um minuto antes de
/// vencer. Deve ser registrado como singleton, para o cache valer entre as chamadas.
/// </summary>
internal sealed class TokenDoMicrosoft
{
	private static readonly TimeSpan Folga = TimeSpan.FromMinutes(1);
	private static readonly TimeSpan ValidadeQuandoOmitida = TimeSpan.FromMinutes(5);

	private readonly HttpClient _http;
	private readonly AgendaOptions _opcoes;
	private readonly TimeProvider _relogio;
	private readonly SemaphoreSlim _trava = new(1, 1);

	private string? _token;
	private DateTimeOffset _validoAte;

	/// <summary>Cria o provedor de token.</summary>
	/// <param name="http">Cliente HTTP do pedido de token.</param>
	/// <param name="opcoes">Configuração da agenda.</param>
	/// <param name="relogio">Relógio; o padrão é o do sistema.</param>
	public TokenDoMicrosoft(HttpClient http, AgendaOptions opcoes, TimeProvider? relogio = null)
	{
		_http = http;
		_opcoes = opcoes;
		_relogio = relogio ?? TimeProvider.System;
	}

	/// <summary>Devolve o token em cache ou pede um novo.</summary>
	/// <param name="ct">Cancelamento.</param>
	public async Task<ResultadoDoToken> ObterAsync(CancellationToken ct)
	{
		await _trava.WaitAsync(ct).ConfigureAwait(false);

		try
		{
			if (_token is not null && _relogio.GetUtcNow() < _validoAte)
			{
				return new ResultadoDoToken(_token, null);
			}

			return await PedirAsync(ct).ConfigureAwait(false);
		}
		finally
		{
			_trava.Release();
		}
	}

	/// <summary>Descarta o token em cache (a agenda o recusou).</summary>
	public void Descartar()
	{
		_trava.Wait();

		try
		{
			_token = null;
		}
		finally
		{
			_trava.Release();
		}
	}

	private async Task<ResultadoDoToken> PedirAsync(CancellationToken ct)
	{
		var microsoft = _opcoes.Microsoft;
		var endereco = $"https://login.microsoftonline.com/{Uri.EscapeDataString(microsoft.TenantId!.Trim())}/oauth2/v2.0/token";

		using var pedido = new HttpRequestMessage(HttpMethod.Post, endereco)
		{
			Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["client_id"] = microsoft.ClientId!.Trim(),
				["client_secret"] = microsoft.ClientSecret!,
				["scope"] = "https://graph.microsoft.com/.default",
				["grant_type"] = "client_credentials"
			})
		};

		HttpResponseMessage resposta;

		try
		{
			resposta = await _http.SendAsync(pedido, ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			return Falha(DesfechoDaAgenda.FalhaPassageira, "A agenda não respondeu ao pedido de acesso.");
		}
		catch (HttpRequestException)
		{
			return Falha(DesfechoDaAgenda.FalhaPassageira, "A agenda não pôde ser alcançada para o pedido de acesso.");
		}

		using (resposta)
		{
			var codigo = (int)resposta.StatusCode;

			if (!resposta.IsSuccessStatusCode)
			{
				return codigo is 408 or 429 or >= 500
					? Falha(DesfechoDaAgenda.FalhaPassageira, $"A agenda não concedeu acesso agora ({codigo}).")
					: Falha(DesfechoDaAgenda.FalhaDefinitiva, $"A agenda recusou as credenciais do aplicativo ({codigo}).");
			}

			try
			{
				var corpo = await resposta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
				using var documento = JsonDocument.Parse(corpo);
				var raiz = documento.RootElement;
				var token = raiz.GetProperty("access_token").GetString();

				if (string.IsNullOrEmpty(token))
				{
					return Falha(DesfechoDaAgenda.FalhaPassageira, "A agenda devolveu uma resposta de acesso inválida.");
				}

				var validade = raiz.TryGetProperty("expires_in", out var segundos) && segundos.TryGetInt32(out var s) && s > 0
					? TimeSpan.FromSeconds(s)
					: ValidadeQuandoOmitida;

				_token = token;
				_validoAte = _relogio.GetUtcNow() + validade - Folga;
				return new ResultadoDoToken(token, null);
			}
			catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
			{
				return Falha(DesfechoDaAgenda.FalhaPassageira, "A agenda devolveu uma resposta de acesso inválida.");
			}
		}
	}

	private static ResultadoDoToken Falha(DesfechoDaAgenda desfecho, string motivo) =>
		new(null, new ResultadoDaAgenda(desfecho, Motivo: motivo));
}
