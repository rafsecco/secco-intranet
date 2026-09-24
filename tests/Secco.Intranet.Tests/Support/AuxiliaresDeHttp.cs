using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Secco.Intranet.Tests.Support;

/// <summary>Auxiliares dos testes de tela: token antifalsificação, formulário e HTML legível.</summary>
public static class AuxiliaresDeHttp
{
	/// <summary>Abre a página e devolve o token antifalsificação do formulário dela.</summary>
	public static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	/// <summary>Monta um corpo de formulário.</summary>
	public static FormUrlEncodedContent Form(params (string Chave, string Valor)[] campos) =>
		new(campos.Select(campo => new KeyValuePair<string, string>(campo.Chave, campo.Valor)));

	/// <summary>O Razor codifica acentos em HTML (<c>&#xE3;</c>); comparar texto exige decodificar.</summary>
	public static string Decodificar(string html) => WebUtility.HtmlDecode(html);
}
