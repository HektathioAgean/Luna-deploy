// @ts-check
// Renderiza as figuras que o Python monta. Nenhuma regra de grafico vive aqui:
// data e layout chegam prontos de plotly.graph_objects via fig.to_json().
//
// O plotly.min.js vem de /vendor/plotly.min.js, servido pelo luna.py a partir
// do pacote plotly instalado. Sem CDN: a rede tem inspecao TLS e o aplicativo
// precisa abrir offline.

import { el } from './componentes.js';

/** @type {Promise<any>|null} */
let carregamento = null;

function carregarPlotly() {
  if (carregamento) return carregamento;
  carregamento = new Promise((resolve, reject) => {
    if (/** @type {any} */ (window).Plotly) {
      resolve(/** @type {any} */ (window).Plotly);
      return;
    }
    const script = document.createElement('script');
    script.src = '/vendor/plotly.min.js';
    script.onload = () => resolve(/** @type {any} */ (window).Plotly);
    script.onerror = () => reject(new Error(
      'plotly.min.js não carregou. Reinstale as dependências: ' +
      'py -m pip install -r requirements.txt'
    ));
    document.head.append(script);
  });
  return carregamento;
}

const CONFIGURACAO = {
  displaylogo: false,
  responsive: true,
  scrollZoom: true,
  locale: 'pt-br',
  modeBarButtonsToRemove: ['lasso2d', 'select2d'],
};

/**
 * @param {{data:any[], layout:object}|null} figura
 * @param {string} [vazio] texto quando nao ha figura
 */
export function grafico(figura, vazio = 'Sem gráfico para exibir.') {
  const caixa = el('div', { classe: 'grafico' }, [
    el('p', { classe: 'grafico-carregando', texto: 'Desenhando...' }),
  ]);

  if (!figura) {
    caixa.replaceChildren(el('p', { classe: 'grafico-carregando', texto: vazio }));
    return caixa;
  }

  carregarPlotly()
    .then((Plotly) => {
      caixa.replaceChildren();
      Plotly.newPlot(caixa, figura.data, figura.layout, CONFIGURACAO);
    })
    .catch((erro) => {
      caixa.replaceChildren(el('p', {
        classe: 'grafico-carregando',
        texto: erro instanceof Error ? erro.message : String(erro),
      }));
    });

  return caixa;
}

/** Libera as figuras de um container antes de trocar de tela. */
export function limparGraficos(container) {
  const Plotly = /** @type {any} */ (window).Plotly;
  if (!Plotly) return;
  container.querySelectorAll('.grafico').forEach((caixa) => {
    if (caixa.querySelector('.js-plotly-plot') || caixa.classList.contains('js-plotly-plot')) {
      Plotly.purge(caixa);
    }
  });
}
