namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Projeta uma publicação rastreada para o DTO de leitura.</summary>
internal static class Projecao
{
	/// <summary>Converte.</summary>
	/// <param name="encontrada">Publicação com os dados do setor.</param>
	internal static PublicacaoDto De(PublicacaoComSetor encontrada) =>
		new(encontrada.Publicacao.Id,
			encontrada.Publicacao.Titulo,
			encontrada.Publicacao.Corpo,
			encontrada.Publicacao.Tipo,
			encontrada.Publicacao.Visibilidade,
			encontrada.Publicacao.Prioridade,
			encontrada.Publicacao.PublicadoEm,
			encontrada.Publicacao.ExpiraEm,
			encontrada.Publicacao.Ativo,
			encontrada.Publicacao.SetorId,
			encontrada.SetorNome,
			encontrada.SetorSlug,
			encontrada.Publicacao.CriadoPor);
}
