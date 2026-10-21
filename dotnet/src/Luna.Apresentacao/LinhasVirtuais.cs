using System.Collections;
using Luna.Application;

namespace Luna.Apresentacao;

/// <summary>Uma linha da grade: a posição na tabela e o texto de cada célula.</summary>
/// <remarks>Igual a outra da mesma lista na mesma posição: a grade reconhece a linha mesmo montada de novo.</remarks>
public sealed class LinhaVirtual
{
    internal LinhaVirtual(LinhasVirtuais dono, int indice, string[] celulas)
    {
        Dono = dono;
        Indice = indice;
        Celulas = celulas;
    }

    internal LinhasVirtuais Dono { get; }
    public int Indice { get; }
    public IReadOnlyList<string> Celulas { get; }

    public override bool Equals(object? obj) => obj is LinhaVirtual outra && outra.Dono == Dono && outra.Indice == Indice;

    public override int GetHashCode() => HashCode.Combine(Dono, Indice);
}

/// <summary>
/// As linhas de uma <see cref="TabelaSaida"/> para a grade virtualizada, sem
/// cópia da tabela: <see cref="Count"/> vem da quantidade e cada linha é
/// montada (<see cref="TabelaSaida.Linha"/> + <see cref="FormatoCelula"/>)
/// só quando a grade a pede. <see cref="IndexOf"/> não percorre a lista.
/// Somente leitura, sem ordenação nem filtro (a ordem é a do motor).
/// </summary>
public sealed class LinhasVirtuais : IList, IReadOnlyList<LinhaVirtual>
{
    // As últimas linhas montadas, por posição: a grade pede a mesma linha mais de uma vez.
    private const int TamanhoDoCache = 256;

    private readonly Func<int, IReadOnlyList<object?>> _linha;
    private readonly LinhaVirtual?[] _cache = new LinhaVirtual?[TamanhoDoCache];

    public LinhasVirtuais(TabelaSaida tabela)
        : this(tabela.Colunas, tabela.Quantidade, tabela.Linha)
    {
    }

    /// <param name="linha">Os valores da linha i, na ordem de <paramref name="colunas"/>.</param>
    public LinhasVirtuais(IReadOnlyList<ColunaSaida> colunas, int quantidade, Func<int, IReadOnlyList<object?>> linha)
    {
        Colunas = colunas;
        Count = quantidade;
        _linha = linha;
    }

    public IReadOnlyList<ColunaSaida> Colunas { get; }
    public int Count { get; }

    /// <summary>Quantas vezes uma linha foi montada a partir da tabela (para medir a virtualização).</summary>
    public long LinhasMontadas { get; private set; }

    public LinhaVirtual this[int indice]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(indice);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(indice, Count);
            ref var guardada = ref _cache[indice % TamanhoDoCache];
            if (guardada?.Indice == indice)
                return guardada;
            var valores = _linha(indice);
            LinhasMontadas++;
            var celulas = new string[Colunas.Count];
            for (int j = 0; j < celulas.Length; j++)
                celulas[j] = FormatoCelula.Texto(Colunas[j], valores[j]);
            return guardada = new LinhaVirtual(this, indice, celulas);
        }
    }

    public int IndexOf(object? item) => item is LinhaVirtual linha && linha.Dono == this ? linha.Indice : -1;

    public IEnumerator<LinhaVirtual> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    bool IList.IsReadOnly => true;
    bool IList.IsFixedSize => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    bool IList.Contains(object? value) => IndexOf(value) >= 0;

    void ICollection.CopyTo(Array array, int index)
    {
        for (int i = 0; i < Count; i++)
            array.SetValue(this[i], index + i);
    }

    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
}
