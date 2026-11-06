using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;

namespace Secco.Intranet.Web.Models.Publicacoes;

/// <summary>Dados do formulário de publicação.</summary>
public sealed class PublicacaoFormViewModel
{
	/// <summary>Identificador; vazio significa publicação nova.</summary>
	public Guid Id { get; set; }

	/// <summary>Título.</summary>
	[Required(ErrorMessage = "O título é obrigatório.")]
	[StringLength(256)]
	public string? Titulo { get; set; }

	/// <summary>Corpo em Markdown; opcional (só o título é obrigatório).</summary>
	[StringLength(20_000, ErrorMessage = "O corpo excede o limite de 20000 caracteres.")]
	public string? Corpo { get; set; }

	/// <summary>Imagem enviada; vazia mantém a atual (edição) ou publica sem imagem (nova).</summary>
	public IFormFile? Imagem { get; set; }

	/// <summary>Descrição da imagem, para quem usa leitor de tela.</summary>
	[StringLength(300, ErrorMessage = "A descrição da imagem passa de 300 caracteres.")]
	public string? ImagemDescricao { get; set; }

	/// <summary>Marca a remoção da imagem atual; só vale na edição e quando não há nova imagem.</summary>
	public bool RemoverImagem { get; set; }

	/// <summary>
	/// Se a publicação em edição já tem imagem. Só de exibição: o controller o recalcula a partir
	/// do que está guardado, nunca do que veio no formulário.
	/// </summary>
	public bool TemImagem { get; set; }

	/// <summary>Natureza.</summary>
	public TipoPublicacao Tipo { get; set; } = TipoPublicacao.Aviso;

	/// <summary>Quem enxerga.</summary>
	public Visibilidade Visibilidade { get; set; } = Visibilidade.Empresa;

	/// <summary>Urgência.</summary>
	public PrioridadePublicacao Prioridade { get; set; } = PrioridadePublicacao.Normal;

	/// <summary>
	/// Entrada no ar. O formulário usa <c>datetime-local</c>, que não carrega offset: o valor
	/// é interpretado no fuso do servidor (limitação registrada no spec).
	/// </summary>
	/// <remarks>
	/// O formato explícito para no minuto. Sem ele o campo nasce mostrando segundos e
	/// milissegundos — <c>02:36:50,955</c> —, precisão que ninguém digita e que a regra do
	/// mural não usa.
	/// </remarks>
	[DataType(DataType.DateTime)]
	[DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
	public DateTime PublicadoEm { get; set; } = TruncarNoMinuto(DateTime.Now);

	/// <summary>Expiração; vazio não expira.</summary>
	[DataType(DataType.DateTime)]
	[DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
	public DateTime? ExpiraEm { get; set; }

	private static DateTime TruncarNoMinuto(DateTime valor) =>
		valor.AddTicks(-(valor.Ticks % TimeSpan.TicksPerMinute));
}

/// <summary>Modelo da aba de avisos do setor.</summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Publicacoes">Publicações do setor, inclusive fora do ar.</param>
/// <param name="PodePublicar">Se o usuário administra este setor.</param>
/// <param name="Form">Dados do formulário.</param>
/// <param name="Agora">Instante usado para calcular a etiqueta de cada publicação.</param>
/// <param name="Titulo">Nome do item da árvore que abriu a página.</param>
/// <param name="Trilho">Setor e ancestrais do item ("Financeiro › Relatórios"), para o subtítulo.</param>
/// <param name="Endereco">Endereço da própria aba, base do link "Editar" e do "Cancelar".</param>
public sealed record SetorAvisosViewModel(
	SetorDto Setor,
	IReadOnlyList<PublicacaoDto> Publicacoes,
	bool PodePublicar,
	PublicacaoFormViewModel Form,
	DateTimeOffset Agora,
	string Titulo,
	string Trilho,
	string Endereco);
