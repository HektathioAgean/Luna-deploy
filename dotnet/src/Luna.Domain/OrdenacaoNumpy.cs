namespace Luna.Domain;

/// <summary>
/// <c>np.argsort(kind="quicksort")</c> do numpy 2.2.6 para chaves inteiras
/// (o <c>aquicksort</c> escalar: introsort com mediana de três, inserção abaixo
/// de 16 elementos e heapsort quando a profundidade estoura). É o que o
/// <c>sort_values</c> do pandas usa numa coluna datetime64; não é estável, e a
/// ordem dos empates é a dele, não a da entrada.
/// </summary>
public static class OrdenacaoNumpy
{
    private const int PequenoQuicksort = 15;

    /// <summary>Índices que ordenam <paramref name="chaves"/> como o numpy.</summary>
    public static int[] ArgsortQuicksort(IReadOnlyList<long> chaves)
    {
        int n = chaves.Count;
        var t = new int[n];
        for (int i = 0; i < n; i++)
            t[i] = i;
        if (n < 2)
            return t;

        int pl = 0, pr = n - 1;
        var pilha = new Stack<(int Esquerda, int Direita, int Profundidade)>();
        int profundidade = BitMaisAlto(n) * 2;
        while (true)
        {
            if (profundidade < 0)
            {
                Heapsort(chaves, t, pl, pr - pl + 1);
            }
            else
            {
                while (pr - pl > PequenoQuicksort)
                {
                    int pm = pl + ((pr - pl) >> 1);
                    if (chaves[t[pm]] < chaves[t[pl]]) (t[pm], t[pl]) = (t[pl], t[pm]);
                    if (chaves[t[pr]] < chaves[t[pm]]) (t[pr], t[pm]) = (t[pm], t[pr]);
                    if (chaves[t[pm]] < chaves[t[pl]]) (t[pm], t[pl]) = (t[pl], t[pm]);
                    long pivo = chaves[t[pm]];
                    int pi = pl, pj = pr - 1;
                    (t[pm], t[pj]) = (t[pj], t[pm]);
                    while (true)
                    {
                        do { pi++; } while (chaves[t[pi]] < pivo);
                        do { pj--; } while (pivo < chaves[t[pj]]);
                        if (pi >= pj)
                            break;
                        (t[pi], t[pj]) = (t[pj], t[pi]);
                    }
                    (t[pi], t[pr - 1]) = (t[pr - 1], t[pi]);
                    // A partição maior vai para a pilha.
                    profundidade--;
                    if (pi - pl < pr - pi)
                    {
                        pilha.Push((pi + 1, pr, profundidade));
                        pr = pi - 1;
                    }
                    else
                    {
                        pilha.Push((pl, pi - 1, profundidade));
                        pl = pi + 1;
                    }
                }
                for (int pi = pl + 1; pi <= pr; pi++)
                {
                    int vi = t[pi];
                    long vp = chaves[vi];
                    int pj = pi;
                    while (pj > pl && vp < chaves[t[pj - 1]])
                    {
                        t[pj] = t[pj - 1];
                        pj--;
                    }
                    t[pj] = vi;
                }
            }
            if (pilha.Count == 0)
                break;
            (pl, pr, profundidade) = pilha.Pop();
        }
        return t;
    }

    // npy_get_msb: posição do bit mais alto.
    private static int BitMaisAlto(int n)
    {
        int bit = 0;
        while ((n >>= 1) != 0)
            bit++;
        return bit;
    }

    // aheapsort do numpy, sobre t[inicio .. inicio + n).
    private static void Heapsort(IReadOnlyList<long> chaves, int[] t, int inicio, int n)
    {
        // Índices do heap a partir de 1, como no C.
        int A(int i) => t[inicio + i - 1];
        void Definir(int i, int valor) => t[inicio + i - 1] = valor;

        for (int l = n >> 1; l > 0; l--)
        {
            int tmp = A(l);
            int i = l, j = l << 1;
            while (j <= n)
            {
                if (j < n && chaves[A(j)] < chaves[A(j + 1)])
                    j++;
                if (chaves[tmp] < chaves[A(j)])
                {
                    Definir(i, A(j));
                    i = j;
                    j += j;
                }
                else
                {
                    break;
                }
            }
            Definir(i, tmp);
        }
        while (n > 1)
        {
            int tmp = A(n);
            Definir(n, A(1));
            n--;
            int i = 1, j = 2;
            while (j <= n)
            {
                if (j < n && chaves[A(j)] < chaves[A(j + 1)])
                    j++;
                if (chaves[tmp] < chaves[A(j)])
                {
                    Definir(i, A(j));
                    i = j;
                    j += j;
                }
                else
                {
                    break;
                }
            }
            Definir(i, tmp);
        }
    }
}
