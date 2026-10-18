using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class FotoDoColaboradorHandlerTests
{
	private static readonly Guid Ana = Guid.NewGuid();

	private readonly PerfisColaboradorFalso _perfis = new();
	private readonly ProcessadorDeImagemFalso _imagens = new();
	private readonly ArquivoStoreFalso _arquivos = new();
	private readonly TrilhaDeAcessoFalsa _trilha = new();
	private readonly LoggerCapturador<FotoDoColaboradorHandler> _logger = new();

	private FotoDoColaboradorHandler Handler()
	{
		_arquivos.ContadorDeSalvamentos = () => _perfis.Salvou;

		return new(_perfis, _imagens, _arquivos, _trilha, _logger);
	}

	private static MemoryStream Upload() => new([9, 9]);

	[Fact]
	public async Task Definir_PrimeiraFoto_GravaNaPastaDoDiretorio_ESalva()
	{
		var perfil = new PerfilColaborador(Ana);
		_perfis.Com(perfil);

		var resultado = await Handler().DefinirAsync(Ana, Upload(), null);

		resultado.IsSuccess.Should().BeTrue();
		_arquivos.Gravados.Should().ContainSingle().Which.Pasta.Should().Be(PastasDeArquivo.FotosDoDiretorio);
		perfil.TemFoto.Should().BeTrue();
		perfil.FotoCaminhoRelativo.Should().Be(_arquivos.Gravados[0].Caminho);
		_perfis.Salvou.Should().BePositive();
		_arquivos.Removidos.Should().BeEmpty();
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.DiretorioFotoDefinir);
		_trilha.Registros[0].Recurso.Should().Be(RecursosDeAuditoria.Diretorio);
		_trilha.Registros[0].RecursoId.Should().Be(Ana.ToString());
	}

	[Fact]
	public async Task Definir_Reenvio_RemoveOAnteriorDepoisDeSalvar()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.DefinirFoto("diretorio/fotos/antiga.bin", "k");
		_perfis.Com(perfil);

		var resultado = await Handler().DefinirAsync(Ana, Upload(), null);

		resultado.IsSuccess.Should().BeTrue();
		_arquivos.Removidos.Should().Equal("diretorio/fotos/antiga.bin");
		_arquivos.SalvamentosNasRemocoes.Should().ContainSingle().Which.Should().BePositive();
		perfil.FotoCaminhoRelativo.Should().Be(_arquivos.Gravados[0].Caminho);
	}

	[Fact]
	public async Task Definir_FalhaAoRemoverOAnterior_SucessoMesmoAssim()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.DefinirFoto("diretorio/fotos/antiga.bin", "k");
		_perfis.Com(perfil);
		_arquivos.FalharAoRemover = true;

		var resultado = await Handler().DefinirAsync(Ana, Upload(), null);

		resultado.IsSuccess.Should().BeTrue();
		_logger.Entradas.Should().ContainSingle(entrada => entrada.Nivel == LogLevel.Warning)
			.Which.Mensagem.Should().NotContain("antiga.bin");
		_logger.Entradas.Should().OnlyContain(entrada => entrada.Excecao == null);
	}

	[Fact]
	public async Task Definir_ImagemInvalida_NaoGravaNada()
	{
		_imagens.FalharCom = IntranetErrors.Diretorio.FotoInvalida;

		var resultado = await Handler().DefinirAsync(Ana, Upload(), null);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Diretorio.FotoInvalida);
		_arquivos.Gravados.Should().BeEmpty();
		_perfis.Perfis.Should().BeEmpty();
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Definir_SemPerfil_CriaOPerfil()
	{
		var resultado = await Handler().DefinirAsync(Ana, Upload(), null);

		resultado.IsSuccess.Should().BeTrue();
		_perfis.Perfis.Should().ContainSingle().Which.TemFoto.Should().BeTrue();
	}

	[Fact]
	public async Task Remover_ApagaOArquivo_EVoltaAsIniciais()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.DefinirFoto("diretorio/fotos/a.bin", "k");
		_perfis.Com(perfil);

		var resultado = await Handler().RemoverAsync(Ana);

		resultado.IsSuccess.Should().BeTrue();
		perfil.TemFoto.Should().BeFalse();
		_arquivos.Removidos.Should().Equal("diretorio/fotos/a.bin");
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.DiretorioFotoRemover);
	}

	[Fact]
	public async Task Remover_SemFoto_SucessoSemTrilha()
	{
		_perfis.Com(new PerfilColaborador(Ana));

		var resultado = await Handler().RemoverAsync(Ana);

		resultado.IsSuccess.Should().BeTrue();
		_trilha.Registros.Should().BeEmpty();
		_arquivos.Removidos.Should().BeEmpty();
	}

	[Fact]
	public async Task Obter_SemFoto_NaoEncontrada()
	{
		var semPerfil = await Handler().ObterAsync(Ana);

		semPerfil.Error.Should().Be(IntranetErrors.Diretorio.FotoNaoEncontrada);

		_perfis.Com(new PerfilColaborador(Ana));

		var semFoto = await Handler().ObterAsync(Ana);

		semFoto.Error.Should().Be(IntranetErrors.Diretorio.FotoNaoEncontrada);
	}

	[Fact]
	public async Task Obter_ETagMudaQuandoAFotoMuda()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.DefinirFoto("diretorio/fotos/a.bin", "k1");
		_perfis.Com(perfil);

		var primeira = (await Handler().ObterAsync(Ana)).Value;
		var repetida = (await Handler().ObterAsync(Ana)).Value;

		perfil.DefinirFoto("diretorio/fotos/b.bin", "k2");

		var segunda = (await Handler().ObterAsync(Ana)).Value;

		primeira.ContentType.Should().Be("image/webp");
		primeira.CaminhoRelativo.Should().Be("diretorio/fotos/a.bin");
		primeira.ChaveEmbrulhada.Should().Be("k1");
		primeira.ETag.Should().MatchRegex("^\"[0-9a-f]{16}\"$");
		primeira.ETag.Should().NotContain("a.bin");
		repetida.ETag.Should().Be(primeira.ETag);
		segunda.ETag.Should().NotBe(primeira.ETag);
	}
}
