namespace Secco.Intranet.Application.Salas;

/// <summary>Informa se o espelhamento das reservas na agenda externa está ligado neste ambiente.</summary>
public interface IEspelhamentoLigado
{
	/// <summary>Verdadeiro quando novas reservas e alterações devem ser espelhadas.</summary>
	bool Ligado { get; }
}

/// <summary>Implementação padrão: sem agenda externa configurada, nada é espelhado.</summary>
internal sealed class EspelhamentoDesligado : IEspelhamentoLigado
{
	/// <inheritdoc />
	public bool Ligado => false;
}
