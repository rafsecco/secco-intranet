using System.Globalization;
using System.Text.RegularExpressions;

namespace Secco.Intranet.Web.Hosting;

/// <summary>
/// Como as telas mostram e leem datas, pela cultura configurada em <c>Intranet:Formatos:Cultura</c>
/// (padrão <c>pt-BR</c>). Os campos de data são texto no padrão da cultura em vez do seletor nativo
/// do navegador, que segue o idioma do navegador e não o da intranet. O formato ISO
/// (<c>aaaa-mm-dd</c>, usado nos links) é sempre aceito na leitura.
/// </summary>
/// <remarks>
/// Base para a troca de idioma: hoje vale para os campos de data das salas, dos vencimentos e dos
/// avisos; as datas apenas exibidas no resto do produto ainda usam dd/mm/aaaa fixo.
/// </remarks>
public sealed class FormatoDeDatas
{
	/// <summary>Chave de configuração da cultura.</summary>
	public const string ChaveDaCultura = "Intranet:Formatos:Cultura";

	/// <summary>Cultura usada quando a configuração não informa nenhuma.</summary>
	public const string CulturaPadrao = "pt-BR";

	private static readonly string[] FormatosIsoDeData = ["yyyy-MM-dd"];

	private static readonly string[] FormatosIsoDeDataEHora =
		["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];

	private readonly string[] _formatosDeData;
	private readonly string[] _formatosDeDataEHora;

	/// <summary>Cria o formato para a cultura informada.</summary>
	/// <param name="cultura">Nome da cultura (ex.: <c>pt-BR</c>).</param>
	/// <exception cref="InvalidOperationException">Cultura inexistente.</exception>
	public FormatoDeDatas(string cultura)
	{
		try
		{
			Cultura = CultureInfo.GetCultureInfo(cultura, predefinedOnly: true);
		}
		catch (CultureNotFoundException excecao)
		{
			throw new InvalidOperationException($"{ChaveDaCultura} não é uma cultura conhecida: '{cultura}'.", excecao);
		}

		PadraoDeData = Normalizar(Cultura.DateTimeFormat.ShortDatePattern);
		PadraoDeHora = "HH:mm";

		// Aceita também dia e mês com um dígito (3/1/2030) e hora com um dígito (9:05).
		var curto = PadraoDeData.Replace("dd", "d", StringComparison.Ordinal).Replace("MM", "M", StringComparison.Ordinal);
		_formatosDeData = [PadraoDeData, curto, .. FormatosIsoDeData];
		_formatosDeDataEHora =
		[
			$"{PadraoDeData} HH:mm", $"{PadraoDeData} H:mm", $"{curto} HH:mm", $"{curto} H:mm", .. FormatosIsoDeDataEHora,
		];

		DicaDeData = Dica(PadraoDeData);
		DicaDeDataEHora = $"{DicaDeData} hh:mm";
		MensagemDeDataInvalida = $"Informe uma data válida no formato {DicaDeData}.";
		MensagemDeDataEHoraInvalida = $"Informe data e hora válidas no formato {DicaDeDataEHora}.";
	}

	/// <summary>Cultura configurada.</summary>
	public CultureInfo Cultura { get; }

	/// <summary>Padrão .NET da data (ex.: <c>dd/MM/yyyy</c>).</summary>
	public string PadraoDeData { get; }

	/// <summary>Padrão .NET da hora, sempre 24 horas.</summary>
	public string PadraoDeHora { get; }

	/// <summary>Dica do campo de data para quem digita (ex.: <c>dd/mm/aaaa</c>).</summary>
	public string DicaDeData { get; }

	/// <summary>Dica do campo de data e hora (ex.: <c>dd/mm/aaaa hh:mm</c>).</summary>
	public string DicaDeDataEHora { get; }

	/// <summary>Mensagem para data que não pôde ser lida.</summary>
	public string MensagemDeDataInvalida { get; }

	/// <summary>Mensagem para data e hora que não puderam ser lidas.</summary>
	public string MensagemDeDataEHoraInvalida { get; }

	/// <summary>Data no padrão da cultura.</summary>
	public string Data(DateOnly data) => data.ToString(PadraoDeData, Cultura);

	/// <summary>Data no padrão da cultura, ou vazio.</summary>
	public string Data(DateOnly? data) => data is { } valor ? Data(valor) : string.Empty;

	/// <summary>Data e hora no padrão da cultura.</summary>
	public string DataEHora(DateTime instante) => instante.ToString($"{PadraoDeData} {PadraoDeHora}", Cultura);

	/// <summary>Data e hora no padrão da cultura, ou vazio.</summary>
	public string DataEHora(DateTime? instante) => instante is { } valor ? DataEHora(valor) : string.Empty;

	/// <summary>Hora em 24 horas.</summary>
	public string Hora(TimeOnly hora) => hora.ToString(PadraoDeHora, CultureInfo.InvariantCulture);

	/// <summary>Dia da semana com inicial maiúscula e a data (ex.: <c>Quinta-feira, 03/01/2030</c>).</summary>
	public string DiaPorExtenso(DateOnly data)
	{
		var semana = data.ToString("dddd", Cultura);

		return $"{semana[..1].ToUpper(Cultura)}{semana[1..]}, {Data(data)}";
	}

	/// <summary>Lê uma data no padrão da cultura ou em ISO.</summary>
	public bool TentarLerData(string? texto, out DateOnly data) =>
		DateOnly.TryParseExact(texto?.Trim(), _formatosDeData, Cultura, DateTimeStyles.None, out data);

	/// <summary>Lê data e hora no padrão da cultura ou em ISO.</summary>
	public bool TentarLerDataEHora(string? texto, out DateTime instante) =>
		DateTime.TryParseExact(texto?.Trim(), _formatosDeDataEHora, Cultura, DateTimeStyles.None, out instante);

	// Campo digitado tem largura fixa: dia e mês com dois dígitos e ano com quatro (M/d/yy vira MM/dd/yyyy).
	private static string Normalizar(string padrao)
	{
		var normalizado = Regex.Replace(padrao, "d+", "dd");
		normalizado = Regex.Replace(normalizado, "M+", "MM");

		return Regex.Replace(normalizado, "y+", "yyyy");
	}

	// dd/MM/yyyy -> dd/mm/aaaa em português; mm/dd/yyyy nas demais (a tradução vem com o projeto de idiomas).
	private string Dica(string padrao)
	{
		var dica = padrao.Replace("M", "m", StringComparison.Ordinal);

		return Cultura.TwoLetterISOLanguageName == "pt" ? dica.Replace("y", "a", StringComparison.Ordinal) : dica;
	}
}
