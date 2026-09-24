using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LeitorDeCsvDoDiretorioTests
{
	[Fact]
	public void PontoEVirgula_LeAsLinhas()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome;cargo;ramal;setor;gestor\nana@x.com;Ana;Diretora;2100;diretoria;\nbruno@x.com;Bruno;;;financeiro;ana@x.com\n");

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Erros.Should().BeEmpty();
		resultado.Value.Linhas.Should().HaveCount(2);
		var ana = resultado.Value.Linhas[0];
		ana.Email.Should().Be("ana@x.com");
		ana.Nome.Should().Be("Ana");
		ana.Cargo.Should().Be("Diretora");
		ana.Ramal.Should().Be("2100");
		ana.SetorSlug.Should().Be("diretoria");
		ana.GestorEmail.Should().BeNull("célula vazia vira nulo — significa 'não alterar'");
		resultado.Value.Linhas[1].GestorEmail.Should().Be("ana@x.com");
	}

	[Fact]
	public void Virgula_TambemFunciona()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email,nome\nana@x.com,Ana\n");

		resultado.Value.Linhas.Should().ContainSingle().Which.Nome.Should().Be("Ana");
	}

	[Fact]
	public void BomEQuebraDeLinhaWindows_SaoIgnorados()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("﻿email;nome\r\nana@x.com;Ana\r\nbruno@x.com;Bruno\r\n");

		resultado.Value.Linhas.Select(l => l.Email).Should().Equal("ana@x.com", "bruno@x.com");
	}

	[Fact]
	public void AspasComODelimitadorDentro_MantemOTextoInteiro()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome;cargo\nana@x.com;\"Ribeiro; Ana\";\"Diretora \"\"Geral\"\"\"\n");

		var linha = resultado.Value.Linhas.Single();
		linha.Nome.Should().Be("Ribeiro; Ana");
		linha.Cargo.Should().Be("Diretora \"Geral\"");
	}

	[Fact]
	public void LinhaEmBranco_EhIgnorada_MasANumeracaoContinuaContando()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome\nana@x.com;Ana\n\n;;\nbruno@x.com;Bruno\n");

		resultado.Value.Linhas.Select(l => l.Numero).Should().Equal(2, 5);
	}

	[Fact]
	public void CabecalhoEmMaiusculasEComEspacos_Aceita()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler(" EMAIL ; Nome \nana@x.com;Ana\n");

		resultado.Value.Linhas.Single().Nome.Should().Be("Ana");
	}

	[Fact]
	public void ColunaDesconhecida_FalhaOArquivoInteiro()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;salario\nana@x.com;10000\n");

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Description.Should().Contain("salario");
	}

	[Fact]
	public void SemAColunaEmail_Falha()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("nome;cargo\nAna;Diretora\n");

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Description.Should().Contain("email");
	}

	[Fact]
	public void CabecalhoComColunaRepetida_Falha()
	{
		LeitorDeCsvDoDiretorio.Ler("email;nome;nome\nana@x.com;A;B\n").IsFailure.Should().BeTrue();
	}

	[Fact]
	public void CelulaAMenos_TrataComoVazia_CelulaAMais_EErroDaLinha()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome;cargo\nana@x.com;Ana\nbruno@x.com;Bruno;Analista;sobrou\n");

		resultado.Value.Linhas.Should().ContainSingle(l => l.Email == "ana@x.com").Which.Cargo.Should().BeNull();
		resultado.Value.Erros.Should().ContainSingle(e => e.Numero == 3);
	}

	[Fact]
	public void EmailVazio_EErroDaLinha_ENaoDoArquivo()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome\n;Sem Email\nana@x.com;Ana\n");

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Erros.Should().ContainSingle(e => e.Numero == 2);
		resultado.Value.Linhas.Should().ContainSingle(l => l.Email == "ana@x.com");
	}

	[Fact]
	public void AspasSemFechar_FalhaOArquivo()
	{
		LeitorDeCsvDoDiretorio.Ler("email;nome\nana@x.com;\"Ana\n").IsFailure.Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   \n  ")]
	public void ArquivoVazio_Falha(string? texto)
	{
		LeitorDeCsvDoDiretorio.Ler(texto).IsFailure.Should().BeTrue();
	}

	[Fact]
	public void AcimaDoLimiteDeTamanho_Falha()
	{
		var enorme = "email;nome\n" + new string('a', LeitorDeCsvDoDiretorio.MaximoDeCaracteres);

		LeitorDeCsvDoDiretorio.Ler(enorme).IsFailure.Should().BeTrue();
	}

	[Fact]
	public void AcimaDoLimiteDeLinhas_Falha_EExatamenteNoLimite_Passa()
	{
		string Arquivo(int linhas) => "email\n" + string.Concat(Enumerable.Range(0, linhas).Select(i => $"u{i}@x.com\n"));

		LeitorDeCsvDoDiretorio.Ler(Arquivo(LeitorDeCsvDoDiretorio.MaximoDeLinhas)).IsSuccess.Should().BeTrue();
		LeitorDeCsvDoDiretorio.Ler(Arquivo(LeitorDeCsvDoDiretorio.MaximoDeLinhas + 1)).IsFailure.Should().BeTrue();
	}

	[Fact]
	public void SoOCabecalho_ArquivoValidoSemLinhas()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome\n");

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Linhas.Should().BeEmpty();
	}
}
