using System.Globalization;
using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Application.Vencimentos;
using Secco.Intranet.Domain.Vencimentos;
using Secco.Intranet.Web.Theming.Contracts;

namespace Secco.Intranet.Web.Models.Vencimentos;

/// <summary>Dados do formulário de cadastro e de edição de um vencimento.</summary>
public sealed class VencimentoFormViewModel
{
	/// <summary>Antecedências sugeridas num vencimento novo.</summary>
	public const string AntecedenciasPadrao = "30, 7, 1";

	/// <summary>Título de exibição; o tamanho máximo é conferido pelo caso de uso.</summary>
	public string? Titulo { get; set; }

	/// <summary>Descrição livre.</summary>
	public string? Descricao { get; set; }

	/// <summary>Data do vencimento (<c>input type="date"</c>).</summary>
	[Required(ErrorMessage = "Informe a data de vencimento.")]
	[DataType(DataType.Date)]
	[DisplayFormat(DataFormatString = "{0:yyyy-MM-dd}", ApplyFormatInEditMode = true)]
	public DateOnly? DataVencimento { get; set; }

	/// <summary>Usuário responsável, destinatário dos avisos.</summary>
	public Guid? ResponsavelId { get; set; }

	/// <summary>Dias de antecedência separados por vírgula, como "30, 7, 1".</summary>
	public string? Antecedencias { get; set; } = AntecedenciasPadrao;

	/// <summary>
	/// Converte o campo de texto nos dias de antecedência. Texto que não seja uma lista de
	/// inteiros devolve <c>false</c> — as faixas (1 a 365, sem repetição) são do domínio.
	/// </summary>
	/// <param name="dias">Dias lidos, na ordem em que foram digitados.</param>
	public bool TentarLerAntecedencias(out IReadOnlyList<int> dias)
	{
		var lidos = new List<int>();
		dias = lidos;

		if (string.IsNullOrWhiteSpace(Antecedencias))
		{
			return false;
		}

		foreach (var parte in Antecedencias.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			if (!int.TryParse(parte, NumberStyles.None, CultureInfo.InvariantCulture, out var dia))
			{
				return false;
			}

			lidos.Add(dia);
		}

		return lidos.Count > 0;
	}

	/// <summary>Formulário preenchido com os dados de um vencimento existente.</summary>
	/// <param name="detalhe">Vencimento a editar.</param>
	public static VencimentoFormViewModel De(VencimentoDetalheDto detalhe)
	{
		ArgumentNullException.ThrowIfNull(detalhe);

		return new VencimentoFormViewModel
		{
			Titulo = detalhe.Titulo,
			Descricao = detalhe.Descricao,
			DataVencimento = detalhe.DataVencimento,
			ResponsavelId = detalhe.ResponsavelId,
			Antecedencias = string.Join(", ", detalhe.Antecedencias),
		};
	}
}

/// <summary>Etiqueta de estado do vencimento, na linguagem do contrato de badge.</summary>
public static class EtiquetaDeVencimento
{
	/// <summary>Texto e variante do estado.</summary>
	/// <param name="estado">Estado calculado para hoje.</param>
	public static BadgeModel Para(EstadoDoVencimento estado) => estado switch
	{
		EstadoDoVencimento.EmDia => new BadgeModel("Em dia", BadgeVariante.Neutro),
		EstadoDoVencimento.AVencer => new BadgeModel("A vencer", BadgeVariante.Aviso),
		EstadoDoVencimento.VenceHoje => new BadgeModel("Vence hoje", BadgeVariante.Perigo),
		EstadoDoVencimento.Vencido => new BadgeModel("Vencido", BadgeVariante.Perigo),
		_ => new BadgeModel("Encerrado", BadgeVariante.Neutro),
	};
}

/// <summary>Lista de vencimentos de um setor.</summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Itens">Vencimentos por data de vencimento.</param>
/// <param name="Responsaveis">Nome de cada responsável que ainda tem acesso ao setor.</param>
/// <param name="PodeEscrever">Se o usuário pode cadastrar e alterar.</param>
/// <param name="IncluiEncerrados">Se a lista traz os encerrados.</param>
/// <param name="Titulo">Nome do item da árvore que abriu a página.</param>
/// <param name="Trilho">Setor e ancestrais do item, para o subtítulo.</param>
public sealed record SetorVencimentosViewModel(
	SetorDto Setor,
	IReadOnlyList<VencimentoResumoDto> Itens,
	IReadOnlyDictionary<Guid, string> Responsaveis,
	bool PodeEscrever,
	bool IncluiEncerrados,
	string Titulo,
	string Trilho);

/// <summary>Página do formulário de cadastro ou de edição.</summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Form">Dados do formulário.</param>
/// <param name="Responsaveis">Quem pode ser responsável.</param>
/// <param name="Id">Vencimento em edição; <c>null</c> no cadastro.</param>
/// <param name="Titulo">Nome do item da árvore que abriu a página.</param>
/// <param name="Trilho">Setor e ancestrais do item, para o subtítulo.</param>
/// <param name="CaminhoDaLista">Caminho da lista na árvore do setor, para o botão Cancelar.</param>
public sealed record VencimentoFormPaginaViewModel(
	SetorDto Setor,
	VencimentoFormViewModel Form,
	IReadOnlyList<ResponsavelPossivelDto> Responsaveis,
	Guid? Id,
	string Titulo,
	string Trilho,
	string CaminhoDaLista);

/// <summary>Detalhe de um vencimento.</summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Detalhe">Dados, estado, próximos avisos e histórico.</param>
/// <param name="ResponsavelNome">Nome do responsável; <c>null</c> quando não há.</param>
/// <param name="ResponsavelSemAcesso">Se o responsável perdeu a Role do setor.</param>
/// <param name="PodeEscrever">Se o usuário pode alterar.</param>
/// <param name="Titulo">Nome do item da árvore que abriu a página.</param>
/// <param name="Trilho">Setor e ancestrais do item, para o subtítulo.</param>
public sealed record VencimentoDetalheViewModel(
	SetorDto Setor,
	VencimentoDetalheDto Detalhe,
	string? ResponsavelNome,
	bool ResponsavelSemAcesso,
	bool PodeEscrever,
	string Titulo,
	string Trilho);
