// @ts-check
// Aba Painel do Cliente: evolucao de tempos de um cliente, janela de entrega e
// a tabela de entregas com as duas linhas de resumo no fim.

import { api } from './api.js';
import { aviso, bloco, el, marcavel, metricas, secao } from './componentes.js';
import { grafico, limparGraficos } from './grafico.js';
import { estado } from './state.js';
import { corpoTabela } from './tabela.js';

// Com milhares de clientes, uma lista nativa com todos trava a tela. O campo
// filtra e a lista mostra so os primeiros resultados.
const MAXIMO_NA_LISTA = 300;

export function abaCliente() {
  const resultado = estado.resultado;

  if (!resultado) {
    return el('div', { classe: 'pagina' }, [
      el('h1', { texto: 'Painel do Cliente' }),
      secao('', [aviso('neutro', 'Processe uma base para abrir o painel.')]),
    ]);
  }

  if (estado.clientes.length === 0) {
    return el('div', { classe: 'pagina' }, [
      el('h1', { texto: 'Painel do Cliente' }),
      secao('', [aviso('atencao',
        'Não há clientes disponíveis após o processamento.')]),
    ]);
  }

  if (!estado.cliente || !estado.clientes.includes(estado.cliente)) {
    estado.cliente = estado.clientes[0];
  }

  const conteudo = el('div');
  let filtro = '';

  const campoBusca = el('input', {
    type: 'text',
    placeholder: `Filtrar entre ${estado.clientes.length.toLocaleString('pt-BR')} clientes`,
    value: '',
    spellcheck: 'false',
    oninput: (/** @type {Event} */ e) => {
      filtro = /** @type {HTMLInputElement} */ (e.target).value.trim().toUpperCase();
      redesenharLista();
    },
  });

  const lista = el('select', {
    size: 8,
    onchange: (/** @type {Event} */ e) => {
      estado.cliente = /** @type {HTMLSelectElement} */ (e.target).value;
      carregar();
    },
  });

  const contagemLista = el('span', { classe: 'dica' });

  function redesenharLista() {
    const filtrados = filtro
      ? estado.clientes.filter((c) => c.toUpperCase().includes(filtro))
      : estado.clientes;
    const recorte = filtrados.slice(0, MAXIMO_NA_LISTA);

    lista.replaceChildren(...recorte.map((c) => el('option', {
      value: c, texto: c, selected: c === estado.cliente ? true : null,
    })));

    contagemLista.textContent = filtrados.length > recorte.length
      ? `${filtrados.length.toLocaleString('pt-BR')} resultados; mostrando os primeiros ${recorte.length}.`
      : `${filtrados.length.toLocaleString('pt-BR')} resultado(s).`;
  }

  function botoesDia() {
    return el('div', { classe: 'abas', role: 'tablist' },
      estado.opcoes.dias_semana.map((/** @type {string} */ dia) => el('button', {
        classe: 'aba',
        type: 'button',
        role: 'tab',
        'aria-selected': dia === estado.diaSemana ? 'true' : 'false',
        texto: dia,
        onclick: () => {
          if (dia === estado.diaSemana) return;
          estado.diaSemana = dia;
          carregar();
        },
      })));
  }

  // Trocar de cliente e de dia depressa dispara pedidos concorrentes, e eles
  // nao voltam na ordem em que sairam. So a resposta do pedido mais recente
  // pode escrever na tela.
  let pedido = 0;

  async function carregar() {
    const meu = ++pedido;
    limparGraficos(conteudo);
    conteudo.replaceChildren(el('div', {}, [
      botoesDia(),
      secao('', [aviso('neutro', `Carregando ${estado.cliente}...`)]),
    ]));

    try {
      const painel = await api.cliente(
        /** @type {string} */ (estado.cliente), estado.diaSemana, estado.rotulosTempo);
      if (meu !== pedido) return;
      conteudo.replaceChildren(desenharPainel(painel, botoesDia()));
    } catch (erro) {
      if (meu !== pedido) return;
      conteudo.replaceChildren(el('div', {}, [
        botoesDia(),
        secao('', [aviso('erro', erro instanceof Error ? erro.message : String(erro))]),
      ]));
    }
  }

  redesenharLista();
  carregar();

  return el('div', { classe: 'pagina' }, [
    el('h1', { texto: 'Painel do Cliente' }),
    el('p', { classe: 'linha-apoio', texto: 'Evolução de tempos por cliente' }),

    secao('Cliente', [
      bloco([
        el('div', { classe: 'campos' }, [
          el('label', { classe: 'campo' }, [
            el('span', { classe: 'rot', texto: 'Buscar' }),
            campoBusca,
            contagemLista,
          ]),
          el('label', { classe: 'campo' }, [
            el('span', { classe: 'rot', texto: 'Selecionar' }),
            lista,
          ]),
          el('div', { classe: 'campo' }, [
            el('span', { classe: 'rot', texto: 'Gráfico' }),
            marcavel('Mostrar rótulos de tempo', estado.rotulosTempo, (v) => {
              estado.rotulosTempo = v;
              carregar();
            }),
          ]),
        ]),
      ]),
    ], 'ic-pessoa'),

    conteudo,
  ]);
}

/**
 * @param {any} painel resposta de /api/cliente
 * @param {Node} abasDia
 */
function desenharPainel(painel, abasDia) {
  const partes = [abasDia];

  if (painel.vazio) {
    partes.push(secao('', [aviso('atencao',
      `Não há dados válidos para ${painel.cliente} com o filtro '${painel.dia_semana || 'TODOS'}'.`)]));
    return el('div', {}, partes);
  }

  const r = painel.resumo;
  const parametros = estado.resultado.parametros;

  partes.push(el('p', { classe: 'linha-apoio',
    texto: `${painel.cliente}  ·  filtro de dia: ${painel.dia_semana}` }));

  partes.push(secao('', [
    metricas([
      { rotulo: 'Qtd. entregas', valor: r.qtd_entregas },
      { rotulo: 'Mediana do tempo', valor: r.mediana_tempo_fmt },
      { rotulo: 'Tempo ideal Q1', valor: r.tempo_ideal_q1_fmt },
      { rotulo: 'Gap Mediana × Q1', valor: r.gap_mediana_q1_fmt,
        delta: `${r.gap_mediana_q1_perc}%` },
      { rotulo: 'Média de Vol_caixas', valor: r.media_vol_caixas_fmt },
      { rotulo: 'Base limpa Boxplot', valor: r.qtd_base_limpa_boxplot },
      { rotulo: 'Outliers Boxplot', valor: r.qtd_outliers_boxplot },
    ]),
  ]));

  if (r.qtd_invalidos_convertidos > 0) {
    partes.push(secao('', [aviso('atencao',
      `Foram encontrados ${r.qtd_invalidos_convertidos} registros de volume que ` +
      'não puderam ser convertidos corretamente e foram exibidos como 0 no gráfico.')]));
  }

  partes.push(secao('', [
    bloco([
      el('p', { classe: 'dica', texto:
        `Validação de Vol_caixas — Total: ${r.qtd_total_volume} | ` +
        `Zeros reais: ${r.qtd_zeros_reais} | Nulos na origem: ${r.qtd_nulos_origem} | ` +
        `Inválidos convertidos: ${r.qtd_invalidos_convertidos}` }),
      el('p', { classe: 'dica', texto:
        `Referências de tempo — Mediana: ${r.mediana_tempo_fmt} | ` +
        `Tempo ideal Q1: ${r.tempo_ideal_q1_fmt} | ` +
        `Gap: ${r.gap_mediana_q1_fmt} (${r.gap_mediana_q1_perc}%) | ` +
        `Método ideal: ${r.metodo_ideal_aplicado}` }),
    ]),
  ]));

  partes.push(secao('Evolução de tempos e volume', [
    grafico(painel.grafico),
  ], 'ic-painel'));

  if (parametros.exibir_janelas_entrega) {
    const janela = painel.janela;
    const blocos = [];

    if (!janela) {
      blocos.push(aviso('atencao',
        'Não foi possível calcular a janela para este cliente com o filtro atual. ' +
        'O gráfico abaixo mostra somente as aberturas/finalizações.'));
    } else {
      blocos.push(metricas([
        { rotulo: 'Início da janela', valor: janela.Janela_Inicio },
        { rotulo: 'Fim da janela', valor: janela.Janela_Fim },
        { rotulo: 'Largura', valor: `${Math.round(Number(janela.Janela_Largura_Min))} min` },
        { rotulo: 'Cobertura real', valor: janela.Cobertura_Real },
        { rotulo: 'Cruza meia-noite', valor: janela.Cruza_MeiaNoite },
        { rotulo: 'Comercial', valor: janela.Comercial },
      ]));
      blocos.push(bloco([
        el('p', { classe: 'dica', texto:
          `Base utilizada: ${janela.Base_Janela} | ` +
          `Período predominante: ${janela.Periodo_Predominante} | ` +
          `Comercial: ${janela.Comercial}` }),
      ]));
    }

    blocos.push(grafico(painel.grafico_aberturas));
    partes.push(secao('Janela de entrega do cliente', blocos, 'ic-relogio'));
  }

  partes.push(secao('Entregas do cliente', [
    el('div', { classe: 'tabela-caixa' }, [
      el('div', { classe: 'tabela-topo' }, [
        el('span', { classe: 'legenda',
          texto: `${painel.tabela.total - 2} entregas + 2 linhas de resumo` }),
      ]),
      // As duas ultimas linhas sao Resumo - Mediana e Resumo - Tempo ideal Q1.
      corpoTabela(painel.tabela, { resumoNoFim: 2 }),
    ]),
  ], 'ic-tabela'));

  return el('div', {}, partes);
}
