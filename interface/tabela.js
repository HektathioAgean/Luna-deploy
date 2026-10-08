// @ts-check
// Tabela paginada. Substitui st.dataframe: a paginacao e do servidor, entao a
// tela nunca recebe a base inteira.

import { api } from './api.js';
import { el, icone } from './componentes.js';

const TAMANHO_PADRAO = 100;

/** @typedef {{total:number, pagina:number, paginas:number, tamanho:number,
 *   primeira_linha?:number, colunas:string[], linhas:any[][]}} Payload */

/** @param {any} valor */
function celula(valor) {
  const numerico = typeof valor === 'number';
  const texto = valor === null || valor === undefined
    ? ''
    : numerico && !Number.isInteger(valor)
      ? String(Math.round(valor * 100) / 100)
      : String(valor);
  return el('td', { classe: numerico ? 'num' : null, texto, title: texto });
}

/**
 * Desenha o corpo da tabela a partir de um payload ja carregado.
 * @param {Payload|null} payload
 * @param {{resumoNoFim?:number}} [opcoes] quantas linhas finais sao resumo
 */
export function corpoTabela(payload, opcoes = {}) {
  if (!payload || payload.total === 0) {
    return el('p', { classe: 'tabela-vazia', texto: 'Sem dados para exibição.' });
  }

  const resumo = opcoes.resumoNoFim || 0;
  const primeiraDeResumo = payload.linhas.length - resumo;

  return el('div', { classe: 'tabela-rolagem' }, [
    el('table', { classe: 'dados' }, [
      el('thead', {}, [
        el('tr', {}, payload.colunas.map((c) => el('th', { texto: c, title: c }))),
      ]),
      el('tbody', {}, payload.linhas.map((linha, indice) =>
        el('tr', { classe: resumo && indice >= primeiraDeResumo ? 'resumo' : null },
          linha.map(celula)))),
    ]),
  ]);
}

/**
 * Tabela ligada a /api/tabela, com navegacao de paginas.
 * Devolve o elemento ja no DOM-pronto; o carregamento e assincrono.
 *
 * @param {string} nome nome da tabela no servidor
 * @param {{tamanho?:number, titulo?:string}} [opcoes]
 */
export function tabelaRemota(nome, opcoes = {}) {
  const tamanho = opcoes.tamanho || TAMANHO_PADRAO;
  let pagina = 1;

  const legenda = el('span', { classe: 'legenda', texto: 'carregando...' });
  const anterior = el('button', {
    classe: 'pagina-passo', type: 'button', disabled: true,
    title: 'Página anterior', 'aria-label': 'Página anterior',
  }, [icone('ic-chevron-esq')]);
  const proxima = el('button', {
    classe: 'pagina-passo', type: 'button', disabled: true,
    title: 'Próxima página', 'aria-label': 'Próxima página',
  }, [icone('ic-chevron')]);

  const corpo = el('div', {}, [
    el('p', { classe: 'tabela-vazia', texto: 'Carregando...' }),
  ]);

  const caixa = el('div', { classe: 'tabela-caixa' }, [
    el('div', { classe: 'tabela-topo' }, [legenda, anterior, proxima]),
    corpo,
  ]);

  async function carregar() {
    anterior.disabled = true;
    proxima.disabled = true;
    try {
      const payload = /** @type {Payload} */ (
        await api.tabela(nome, pagina, tamanho));
      pagina = payload.pagina;
      corpo.replaceChildren(corpoTabela(payload));
      if (payload.total === 0) {
        legenda.textContent = '0 linhas';
        return;
      }
      const primeira = payload.primeira_linha || 1;
      const ultima = primeira + payload.linhas.length - 1;
      legenda.textContent =
        `${primeira}-${ultima} de ${payload.total} linhas` +
        (payload.paginas > 1 ? `  ·  página ${payload.pagina}/${payload.paginas}` : '');
      anterior.disabled = payload.pagina <= 1;
      proxima.disabled = payload.pagina >= payload.paginas;
    } catch (erro) {
      legenda.textContent = 'falhou';
      corpo.replaceChildren(el('p', {
        classe: 'tabela-vazia',
        texto: `Não foi possível carregar: ${erro instanceof Error ? erro.message : erro}`,
      }));
    }
  }

  anterior.addEventListener('click', () => { pagina -= 1; carregar(); });
  proxima.addEventListener('click', () => { pagina += 1; carregar(); });
  carregar();

  return caixa;
}
