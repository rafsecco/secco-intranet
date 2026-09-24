using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// O adaptador da gestão de acesso. As garantias são as dos demais adaptadores de borda — o
/// timeout do HttpClient não pode escapar como exceção —, mais o mapeamento de 404/409 para os
/// erros de negócio e a confirmação de que cada chamada leva o tenant e os ids certos.
/// </summary>
public class SecureGateGestaoDeAcessoTests
{
	private static readonly Guid Tenant = Guid.NewGuid();

	private sealed class TenantContextFalso(Guid? tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => tenantId is not null;
	}

	/// <summary>
	/// Herda do client gerado em vez de implementar <c>ISecureGateClient</c> à mão: a interface
	/// ganha métodos a cada release da plataforma, e um fake que a implementa inteira quebra a
	/// compilação a cada bump de pacote.
	/// </summary>
	private sealed class ClientFalso() : SecureGateClient(new HttpClient())
	{
		public List<string> Chamadas { get; } = [];

		public Exception? Falha { get; set; }

		public RoleDetailDto? Papel { get; set; }

		public PagedResultOfRoleMemberDto? Membros { get; set; }

		public ICollection<UserDto> Usuarios { get; set; } = [];

		public UserDetailDto? Usuario { get; set; }

		private Task Registrar(string chamada)
		{
			Chamadas.Add(chamada);

			return Falha is null ? Task.CompletedTask : Task.FromException(Falha);
		}

		public override async Task<ICollection<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken)
		{
			await Registrar($"ListRoles:{tenantId}");

			return [new RoleDto { Name = "financeiro-admin", Permissions = ["a:read", "a:write"] }];
		}

		public override async Task<RoleDetailDto> GetRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken)
		{
			await Registrar($"GetRole:{tenantId}:{role}");

			return Papel!;
		}

		public override async Task<PagedResultOfRoleMemberDto> ListRoleMembersAsync(
			Guid tenantId, string role, int? page, int? size, CancellationToken cancellationToken)
		{
			await Registrar($"ListRoleMembers:{tenantId}:{role}:{page}:{size}");

			return Membros!;
		}

		public override async Task<ICollection<UserDto>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken)
		{
			await Registrar($"ListUsers:{tenantId}");

			return Usuarios;
		}

		public override async Task<UserDetailDto> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
		{
			await Registrar($"GetUser:{tenantId}:{userId}");

			return Usuario!;
		}

		public override async Task<RoleDto> CreateRoleAsync(Guid tenantId, CreateRoleRequest body, CancellationToken cancellationToken)
		{
			await Registrar($"CreateRole:{tenantId}:{body.Name}");

			return new RoleDto { Name = body.Name };
		}

		public override Task DeleteRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken) =>
			Registrar($"DeleteRole:{tenantId}:{role}");

		public override Task AddUserRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken) =>
			Registrar($"AddUserRole:{tenantId}:{userId}:{role}");

		public override Task RemoveUserRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken) =>
			Registrar($"RemoveUserRole:{tenantId}:{userId}:{role}");

		public override Task DeactivateUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			Registrar($"DeactivateUser:{tenantId}:{userId}");

		public override Task ActivateUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			Registrar($"ActivateUser:{tenantId}:{userId}");

		public override Task RevokeUserSessionsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			Registrar($"RevokeUserSessions:{tenantId}:{userId}");
	}

	private static SecureGateGestaoDeAcesso Montar(ClientFalso client, Guid? tenant = null) =>
		new(client, new TenantContextFalso(tenant ?? Tenant), NullLogger<SecureGateGestaoDeAcesso>.Instance);

	private static ApiException Api(int status) =>
		new("erro", status, string.Empty, new Dictionary<string, IEnumerable<string>>(), null!);

	[Fact]
	public async Task ListarPerfis_MapeiaNomeTipoEPermissoes()
	{
		var client = new ClientFalso();

		var resultado = await Montar(client).ListarPerfisAsync();

		var perfil = resultado.Value.Should().ContainSingle().Subject;
		perfil.Should().Be(new PerfilDto("financeiro-admin", 2, TipoDePerfil.Setor, false));
		client.Chamadas.Should().Equal($"ListRoles:{Tenant}");
	}

	[Fact]
	public async Task ObterPerfil_MapeiaDetalhe()
	{
		var client = new ClientFalso { Papel = new RoleDetailDto { Name = "x", Permissions = ["a:read"], IsReserved = true, MemberCount = 3 } };

		var resultado = await Montar(client).ObterPerfilAsync("x");

		var dto = resultado.Value;
		dto.Nome.Should().Be("x");
		dto.Permissoes.Should().Equal("a:read");
		dto.Reservado.Should().BeTrue();
		dto.TotalDeMembros.Should().Be(3);
		dto.Tipo.Should().Be(TipoDePerfil.Comum);
		client.Chamadas.Should().Equal($"GetRole:{Tenant}:x");
	}

	[Fact]
	public async Task ObterPerfil_404_VirasPerfilNaoEncontrado()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(404) }).ObterPerfilAsync("x");

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task ListarMembros_PedePaginaDe100_EMapeiaSituacao()
	{
		var usuario = Guid.NewGuid();
		var client = new ClientFalso
		{
			Membros = new PagedResultOfRoleMemberDto
			{
				Items = [new RoleMemberDto { UserId = usuario, Email = "a@x.com", Status = "LockedOut" }],
				Page = 2,
				Size = 100,
				TotalCount = 101,
				TotalPages = 2,
			},
		};

		var resultado = await Montar(client).ListarMembrosAsync("x", 2);

		client.Chamadas.Should().Equal($"ListRoleMembers:{Tenant}:x:2:100");
		resultado.Value.Total.Should().Be(101);
		resultado.Value.TotalDePaginas.Should().Be(2);
		resultado.Value.Itens.Should().ContainSingle().Which
			.Should().Be(new MembroDoPerfilDto(usuario, "a@x.com", SituacaoDoUsuario.Bloqueado));
	}

	[Theory]
	[InlineData("Active", SituacaoDoUsuario.Ativo)]
	[InlineData("active", SituacaoDoUsuario.Ativo)]
	[InlineData("Deactivated", SituacaoDoUsuario.Desativado)]
	[InlineData("LockedOut", SituacaoDoUsuario.Bloqueado)]
	[InlineData("Qualquer", SituacaoDoUsuario.Desconhecida)]
	[InlineData(null, SituacaoDoUsuario.Desconhecida)]
	public async Task ListarUsuarios_MapeiaSituacao(string? status, SituacaoDoUsuario esperada)
	{
		var client = new ClientFalso { Usuarios = [new UserDto { Id = Guid.NewGuid(), Email = "a@x.com", Status = status!, Roles = ["r"] }] };

		var resultado = await Montar(client).ListarUsuariosAsync();

		resultado.Value.Single().Situacao.Should().Be(esperada);
	}

	[Fact]
	public async Task ObterUsuario_MapeiaDetalhe()
	{
		var id = Guid.NewGuid();
		var client = new ClientFalso
		{
			Usuario = new UserDetailDto
			{
				Id = id,
				Email = "a@x.com",
				Status = "Active",
				Roles = ["r1"],
				EffectivePermissions = ["a:read"],
				ExternalLogins = ["EntraId"],
				TwoFactorEnabled = true,
			},
		};

		var resultado = await Montar(client).ObterUsuarioAsync(id);

		var dto = resultado.Value;
		dto.Perfis.Should().Equal("r1");
		dto.Permissoes.Should().Equal("a:read");
		dto.LoginsExternos.Should().Equal("EntraId");
		dto.DoisFatoresAtivo.Should().BeTrue();
		dto.Situacao.Should().Be(SituacaoDoUsuario.Ativo);
	}

	[Fact]
	public async Task ObterUsuario_404_VirasUsuarioNaoEncontrado()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(404) }).ObterUsuarioAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}

	[Fact]
	public async Task Escritas_LevamOTenantEOsIdsCertos()
	{
		var client = new ClientFalso();
		var gestao = Montar(client);
		var usuario = Guid.NewGuid();

		(await gestao.CriarPerfilAsync("p")).IsSuccess.Should().BeTrue();
		(await gestao.ExcluirPerfilAsync("p")).IsSuccess.Should().BeTrue();
		(await gestao.AtribuirPerfilAsync(usuario, "p")).IsSuccess.Should().BeTrue();
		(await gestao.RetirarPerfilAsync(usuario, "p")).IsSuccess.Should().BeTrue();
		(await gestao.DesativarUsuarioAsync(usuario)).IsSuccess.Should().BeTrue();
		(await gestao.ReativarUsuarioAsync(usuario)).IsSuccess.Should().BeTrue();
		(await gestao.EncerrarSessoesAsync(usuario)).IsSuccess.Should().BeTrue();

		client.Chamadas.Should().Equal(
			$"CreateRole:{Tenant}:p",
			$"DeleteRole:{Tenant}:p",
			$"AddUserRole:{Tenant}:{usuario}:p",
			$"RemoveUserRole:{Tenant}:{usuario}:p",
			$"DeactivateUser:{Tenant}:{usuario}",
			$"ActivateUser:{Tenant}:{usuario}",
			$"RevokeUserSessions:{Tenant}:{usuario}");
	}

	[Fact]
	public async Task CriarPerfil_409_VirasPerfilJaExiste()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(409) }).CriarPerfilAsync("p");

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilJaExiste);
	}

	[Fact]
	public async Task ExcluirPerfil_409_VirasPerfilComMembros()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(409) }).ExcluirPerfilAsync("p");

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilComMembros);
	}

	[Fact]
	public async Task DesativarUsuario_409_VirasDesativacaoRecusada()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(409) }).DesativarUsuarioAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Acesso.DesativacaoRecusada);
	}

	[Fact]
	public async Task AtribuirPerfil_404_VirasUsuarioNaoEncontrado()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(404) }).AtribuirPerfilAsync(Guid.NewGuid(), "p");

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}

	[Fact]
	public async Task ErroInesperadoDaPlataforma_ViraIndisponivel_SemVazarDetalhe()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(500) }).ListarPerfisAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task FalhaDeRede_ViraIndisponivel()
	{
		var resultado = await Montar(new ClientFalso { Falha = new HttpRequestException("caiu") }).ListarUsuariosAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Timeout_ViraIndisponivel_NaoEscapaComoExcecao()
	{
		var resultado = await Montar(new ClientFalso { Falha = new TaskCanceledException("Timeout do HttpClient.") })
			.AtribuirPerfilAsync(Guid.NewGuid(), "p");

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task TimeoutEmLeitura_ViraIndisponivel_NaoEscapaComoExcecao()
	{
		var resultado = await Montar(new ClientFalso { Falha = new TaskCanceledException("Timeout do HttpClient.") })
			.ListarPerfisAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task CancelamentoDoChamador_Relanca()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		var client = new ClientFalso { Falha = new OperationCanceledException(cts.Token) };

		var acao = async () => await Montar(client).ListarPerfisAsync(cts.Token);

		await acao.Should().ThrowAsync<OperationCanceledException>(
			"cancelamento pedido pelo chamador não é falha de infraestrutura");
	}

	[Fact]
	public async Task TenantNaoResolvido_ViraIndisponivel_SemChamarAPlataforma()
	{
		var client = new ClientFalso();
		var gestao = new SecureGateGestaoDeAcesso(client, new TenantContextFalso(null), NullLogger<SecureGateGestaoDeAcesso>.Instance);

		var resultado = await gestao.ListarPerfisAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		client.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task GestaoIndisponivel_ResponderNaoConfigurado_EmTudo()
	{
		var gestao = new GestaoDeAcessoIndisponivel();

		(await gestao.ListarPerfisAsync()).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ObterPerfilAsync("x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ListarMembrosAsync("x", 1)).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ListarUsuariosAsync()).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ObterUsuarioAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.CriarPerfilAsync("x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ExcluirPerfilAsync("x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.AtribuirPerfilAsync(Guid.NewGuid(), "x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.RetirarPerfilAsync(Guid.NewGuid(), "x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.DesativarUsuarioAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ReativarUsuarioAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.EncerrarSessoesAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
	}
}
