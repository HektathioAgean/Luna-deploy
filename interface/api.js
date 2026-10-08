// @ts-check
// Acesso as rotas do luna.py. Toda falha vira Error com a mensagem do servidor.

/**
 * @param {string} rota
 * @param {RequestInit} [opcoes]
 */
async function pedir(rota, opcoes) {
  const resposta = await fetch(rota, opcoes);
  let dados = null;
  try {
    dados = await resposta.json();
  } catch {
    throw new Error(`Resposta invalida de ${rota} (HTTP ${resposta.status}).`);
  }
  if (!resposta.ok) {
    throw new Error(dados?.erro || dados?.msg || `HTTP ${resposta.status}`);
  }
  return dados;
}

/** @param {object} corpo */
function postar(rota, corpo) {
  return pedir(rota, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(corpo || {}),
  });
}

export const api = {
  opcoes: () => pedir('/api/opcoes'),
  estado: () => pedir('/api/estado'),
  resultado: () => pedir('/api/resultado'),
  clientes: () => pedir('/api/clientes'),

  /** @param {string} caminho */
  procurar: (caminho) =>
    pedir(`/api/procurar?caminho=${encodeURIComponent(caminho || '')}`),

  /** @param {string} nome @param {number} pagina @param {number} tamanho */
  tabela: (nome, pagina, tamanho) =>
    pedir(`/api/tabela?nome=${encodeURIComponent(nome)}&pagina=${pagina}&tamanho=${tamanho}`),

  /** @param {string} cod @param {string} dia @param {boolean} rotulos */
  cliente: (cod, dia, rotulos) =>
    pedir(
      `/api/cliente?cod=${encodeURIComponent(cod)}` +
      `&dia=${encodeURIComponent(dia)}&rotulos=${rotulos ? 1 : 0}`
    ),

  /** @param {object} parametros */
  processar: (parametros) => postar('/api/processar', parametros),

  /** @param {string} formato @param {string} destino */
  exportar: (formato, destino) => postar('/api/exportar', { formato, destino }),

  encerrar: () => postar('/api/encerrar', {}),
};

/**
 * Acompanha o processamento por SSE.
 * @param {(estagio:string, segundos:number) => void} aoEstagio
 * @param {(erro:string|null, segundos:number) => void} aoFim
 * @returns {EventSource}
 */
export function acompanhar(aoEstagio, aoFim) {
  const fonte = new EventSource('/api/progresso');

  fonte.addEventListener('estagio', (evento) => {
    const dado = JSON.parse(/** @type {MessageEvent} */ (evento).data);
    aoEstagio(dado.estagio, dado.segundos);
  });

  fonte.addEventListener('fim', (evento) => {
    const dado = JSON.parse(/** @type {MessageEvent} */ (evento).data);
    fonte.close();
    aoFim(dado.erro, dado.segundos);
  });

  // EventSource reconecta sozinho ao fim do stream; o close acima evita isso,
  // mas uma queda real da conexao cai aqui.
  fonte.addEventListener('error', () => {
    if (fonte.readyState === EventSource.CLOSED) return;
    fonte.close();
    aoFim('Conexao com o servidor caiu durante o processamento.', 0);
  });

  return fonte;
}
