using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.EntityFrameworkCore.Cryptography;
using Secco.SharedKernel.Entities;

namespace Secco.Intranet.Infrastructure.Armazenamento;

/// <summary>Quantas chaves de uma coluna foram trocadas e quantas ficaram de fora.</summary>
/// <param name="Tabela">Nome da tabela no banco; serve só ao log.</param>
/// <param name="Reembrulhadas">Chaves trocadas para a chave mestra ativa.</param>
/// <param name="SemChaveConhecida">Chaves que nenhuma chave mestra configurada abre.</param>
internal sealed record ContagemDaColuna(string Tabela, int Reembrulhadas, int SemChaveConhecida);

/// <summary>Resultado de uma passada de reembrulho no banco de um tenant.</summary>
/// <param name="Colunas">Contagem por coluna de chave.</param>
internal sealed record ResultadoDoReembrulho(IReadOnlyList<ContagemDaColuna> Colunas)
{
	/// <summary>Total de chaves trocadas.</summary>
	public int Reembrulhadas => Colunas.Sum(coluna => coluna.Reembrulhadas);

	/// <summary>Total de chaves que nenhuma chave mestra configurada abre.</summary>
	public int SemChaveConhecida => Colunas.Sum(coluna => coluna.SemChaveConhecida);
}

/// <summary>
/// Reembrulha, para a chave mestra ativa, toda chave de arquivo embrulhada por uma chave aposentada.
/// Só as chaves mudam: nenhum arquivo é lido nem regravado.
/// </summary>
/// <param name="cipher">Cifrador de arquivos com a chave ativa e as aposentadas.</param>
internal sealed class ReembrulhoDeChavesDeArquivo(ISeccoFileCipher cipher)
{
	/// <summary>Passa as quatro colunas de chave embrulhada e troca as que não estão em dia.</summary>
	/// <param name="context">Contexto do tenant.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	public async Task<ResultadoDoReembrulho> ReembrulharAsync(IntranetDbContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		var colunas = new List<ContagemDaColuna>
		{
			await ReembrulharColunaAsync<Documento>(context, "tb_documentos", nameof(Documento.ChaveEmbrulhada), cancellationToken),
			await ReembrulharColunaAsync<Publicacao>(context, "tb_publicacoes", nameof(Publicacao.ImagemChaveEmbrulhada), cancellationToken),
			await ReembrulharColunaAsync<ImagemNoTexto>(context, "tb_imagens_no_texto", nameof(ImagemNoTexto.ChaveEmbrulhada), cancellationToken),
			await ReembrulharColunaAsync<PerfilColaborador>(context, "tb_perfis_colaboradores", nameof(PerfilColaborador.FotoChaveEmbrulhada), cancellationToken),
		};

		return new ResultadoDoReembrulho(colunas);
	}

	/// <summary>
	/// Troca a chave da linha só se ela ainda for a que foi lida, para não pisar numa troca de arquivo
	/// que tenha acontecido entre a leitura e a gravação.
	/// </summary>
	/// <returns><see langword="true"/> quando uma linha foi atualizada.</returns>
	internal static async Task<bool> TrocarSeAindaForAsync<TEntidade>(
		IntranetDbContext context,
		string propriedade,
		Guid id,
		string chaveLida,
		string chaveNova,
		CancellationToken cancellationToken)
		where TEntidade : BaseEntity
	{
		var atualizadas = await context.Set<TEntidade>()
			.Where(e => e.Id == id && EF.Property<string>(e, propriedade) == chaveLida)
			.ExecuteUpdateAsync(s => s.SetProperty(e => EF.Property<string>(e, propriedade), chaveNova), cancellationToken);

		return atualizadas == 1;
	}

	private async Task<ContagemDaColuna> ReembrulharColunaAsync<TEntidade>(
		IntranetDbContext context,
		string tabela,
		string propriedade,
		CancellationToken cancellationToken)
		where TEntidade : BaseEntity
	{
		var lidas = await context.Set<TEntidade>()
			.AsNoTracking()
			.Where(e => EF.Property<string>(e, propriedade) != null && EF.Property<string>(e, propriedade) != string.Empty)
			.Select(e => new { e.Id, Chave = EF.Property<string>(e, propriedade) })
			.ToListAsync(cancellationToken);

		var reembrulhadas = 0;
		var semChave = 0;

		foreach (var linha in lidas)
		{
			if (cipher.IsKeyWrappedWithActiveKey(linha.Chave))
			{
				continue;
			}

			string nova;
			try
			{
				nova = cipher.RewrapKey(linha.Chave);
			}
			catch (SeccoSecretCipherException)
			{
				semChave++;
				continue;
			}

			if (await TrocarSeAindaForAsync<TEntidade>(context, propriedade, linha.Id, linha.Chave, nova, cancellationToken))
			{
				reembrulhadas++;
			}
		}

		return new ContagemDaColuna(tabela, reembrulhadas, semChave);
	}
}
