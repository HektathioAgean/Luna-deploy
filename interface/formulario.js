// @ts-check
// Entrada do Luna: escolha da base por caminho e os parametros do processamento.
// Reproduz o formulario que vivia na sidebar do Streamlit, com a diferenca de
// que a base agora e um caminho qualquer do disco ou da rede.

import {
  bloco, campo, el, faixa, icone, marcavel, numero, secao, selecao,
} from './componentes.js';
import { criarNavegador } from './navegador.js';
import { estado, formularioMudou } from './state.js';

/** @param {() => void} aoProcessar */
export function formularioEntrada(aoProcessar) {
  const form = estado.formulario;
  if (!form) return el('div');

  const campoCaminho = el('input', {
    type: 'text',
    value: form.caminho,
    placeholder: 'V:\\Logistica\\...\\base.csv   ou clique em Procurar',
    spellcheck: 'false',
    oninput: (/** @type {Event} */ e) => {
      form.caminho = /** @type {HTMLInputElement} */ (e.target).value;
      atualizarAcao();
    },
  });

  const navegador = criarNavegador({
    aoEscolherArquivo: (caminho) => {
      campoCaminho.value = caminho;
      form.caminho = caminho;
      atualizarAcao();
    },
  });

  const botaoProcurar = el('button', {
    classe: 'botao', type: 'button',
    onclick: () => navegador.alternar(campoCaminho.value.trim()),
  }, [icone('ic-pasta'), 'Procurar']);

  const recado = el('span', { classe: 'recado' });
  const botao = el('button', {
    classe: 'botao principal', type: 'button',
    onclick: () => {
      form.caminho = campoCaminho.value.trim();
      aoProcessar();
    },
  }, [icone('ic-play'), 'Processar base']);

  function atualizarAcao() {
    const vazio = !campoCaminho.value.trim();
    botao.disabled = vazio || estado.rodando;
    if (estado.rodando) recado.textContent = 'Processando...';
    else if (vazio) recado.textContent = 'Informe o caminho da base.';
    else if (formularioMudou()) {
      recado.textContent = 'Há alterações de parâmetro ainda não processadas.';
    } else recado.textContent = '';
  }

  atualizarAcao();

  return el('div', {}, [
    secao('Base', [
      bloco([
        campo('Caminho do arquivo',
          el('div', { classe: 'linha-caminho' }, [campoCaminho, botaoProcurar]),
          'Aceita .csv, .xlsx e .xlsm, em qualquer pasta do disco ou da rede.'),
        navegador.elemento,
      ]),
    ], 'ic-arquivo'),

    secao('Parâmetros', [
      bloco([
        el('div', { classe: 'campos' }, [
          campo('Tempo mínimo de expurgo (s)',
            numero(form.tempo_min_expurgo,
              (v) => { form.tempo_min_expurgo = v; atualizarAcao(); }),
            'Abaixo disto a entrega vai para Expurgados.'),
          campo('Tempo máximo para anomalia (s)',
            numero(form.tempo_max_anomalia,
              (v) => { form.tempo_max_anomalia = v; atualizarAcao(); }),
            'Acima disto a entrega vai para Anomalias.'),
          campo('Eventos prévios',
            numero(form.eventos_previos,
              (v) => { form.eventos_previos = v; atualizarAcao(); }),
            'Quantas entregas mais recentes entram na mediana.'),
          campo('Mínimo de apontamentos por cliente',
            numero(form.minimo_apontamentos,
              (v) => { form.minimo_apontamentos = v; atualizarAcao(); }),
            'Abaixo disto aplica o tempo padrão.'),
          campo('Tempo padrão para poucos apontamentos (s)',
            numero(form.tempo_padrao_poucos_apontamentos,
              (v) => { form.tempo_padrao_poucos_apontamentos = v; atualizarAcao(); })),
          faixa('Ajuste percentual', form.ajuste_percentual,
            (v) => { form.ajuste_percentual = v; atualizarAcao(); },
            { min: -20, max: 100, passo: 1, sufixo: '%' }),
        ]),
      ]),
    ], 'ic-relogio'),

    secao('Janela de entrega', [
      bloco([
        el('div', { classe: 'campos' }, [
          el('div', { classe: 'campo' }, [
            el('span', { classe: 'rot', texto: 'Cálculo' }),
            marcavel('Exibir janelas de entrega', form.exibir_janelas_entrega,
              (v) => { form.exibir_janelas_entrega = v; atualizarAcao(); }),
          ]),
          faixa('Cobertura da janela', form.cobertura_janela,
            (v) => { form.cobertura_janela = v; atualizarAcao(); },
            { min: 50, max: 95, passo: 5, sufixo: '%' }),
          campo('Base para cálculo da janela',
            selecao(estado.opcoes.bases_janela, form.base_janela,
              (v) => { form.base_janela = v; atualizarAcao(); })),
        ]),
        el('div', { classe: 'rodape-acao' }, [recado, botao]),
      ]),
    ], 'ic-relogio'),
  ]);
}
