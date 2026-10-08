// @ts-check
// Bootstrap da interface: tema, topbar, sidebar e troca de secao.
// Nenhum calculo acontece aqui; tudo vem das rotas do luna.py.

import { acompanhar, api } from './api.js';
import { el, icone } from './componentes.js';
import { estado, SECOES, assinar, avisar } from './state.js';
import { limparGraficos } from './grafico.js';

import { abaBase } from './aba_base.js';
import { abaValidacao } from './aba_validacao.js';
import { abaProcessamento } from './aba_processamento.js';
import { abaCliente } from './aba_cliente.js';
import { abaResultados } from './aba_resultados.js';
import { abaExportacao } from './aba_exportacao.js';

const topbar = /** @type {HTMLElement} */ (document.getElementById('topbar'));
const sidebar = /** @type {HTMLElement} */ (document.getElementById('sidebar'));
const workspace = /** @type {HTMLElement} */ (document.getElementById('workspace'));

// A paleta mora em config.py THEME; aqui ela so e aplicada.
function aplicarTema(tema) {
  if (!tema) return;
  const raiz = document.documentElement;
  const mapa = {
    bg: '--background', panel: '--panel', text: '--text', accent: '--accent',
  };
  for (const [chave, variavel] of Object.entries(mapa)) {
    if (tema[chave]) raiz.style.setProperty(variavel, tema[chave]);
  }
}

function desenharTopbar() {
  const classe = estado.rodando ? 'rodando'
    : estado.erro ? 'erro'
    : estado.processado ? 'ok' : '';
  const texto = estado.rodando ? 'processando'
    : estado.erro ? 'falhou'
    : estado.processado ? 'base processada' : 'nenhuma base';

  topbar.replaceChildren(
    el('div', { classe: 'marca' }, [
      'LUNA',
      el('span', { texto: 'Tempos operacionais' }),
    ]),
    estado.resultado
      ? el('div', { classe: 'base-atual', texto: estado.resultado.caminho,
          title: estado.resultado.caminho })
      : el('div', { classe: 'base-atual', texto: '127.0.0.1 — somente local' }),
    el('div', { classe: `estado ${classe}` }, [
      el('span', { classe: 'ponto' }),
      el('span', { texto }),
      estado.segundos
        ? el('span', { texto: `${estado.segundos}s`, classe: 'base-atual' })
        : null,
    ]),
    el('button', {
      classe: 'encerrar', type: 'button', texto: 'Encerrar',
      onclick: encerrar,
    }),
  );
}

function desenharSidebar() {
  sidebar.replaceChildren(
    el('div', { classe: 'nav' }, [
      el('p', { classe: 'nav-titulo', texto: 'ANÁLISE' }),
      ...SECOES.map((secao) => {
        // Antes de processar, só a aba Base tem conteúdo.
        const bloqueada = secao.id !== 'base' && !estado.processado;
        const conta = contagemDaSecao(secao.id);
        return el('button', {
          classe: `nav-item${estado.secao === secao.id ? ' ativo' : ''}`,
          type: 'button',
          disabled: bloqueada ? true : null,
          title: bloqueada ? 'Processe uma base primeiro' : secao.rotulo,
          onclick: () => irPara(secao.id),
        }, [
          icone(secao.icone),
          el('span', { texto: secao.rotulo }),
          conta !== null ? el('span', { classe: 'conta', texto: conta }) : null,
        ]);
      }),
    ]),
    el('div', { classe: 'nav-rodape', texto: `porta ${estado.opcoes?.porta ?? ''}` }),
  );
}

/** @param {string} id */
function contagemDaSecao(id) {
  const resultado = estado.resultado;
  if (!resultado) return null;
  if (id === 'cliente') return estado.clientes.length.toLocaleString('pt-BR');
  if (id === 'resultados') {
    return resultado.contagens.medianas.toLocaleString('pt-BR');
  }
  if (id === 'validacao') return resultado.validacao.is_valid ? 'ok' : '!';
  return null;
}

function desenharWorkspace() {
  limparGraficos(workspace);
  const paginas = {
    base: () => abaBase(processar),
    validacao: abaValidacao,
    processamento: abaProcessamento,
    cliente: abaCliente,
    resultados: abaResultados,
    exportacao: abaExportacao,
  };
  workspace.replaceChildren(paginas[estado.secao]());
  workspace.scrollTop = 0;
}

/** @param {string} id */
function irPara(id) {
  if (estado.secao === id) return;
  estado.secao = /** @type {any} */ (id);
  avisar();
}

async function processar() {
  const parametros = estado.formulario;
  if (!parametros || !parametros.caminho) return;

  estado.rodando = true;
  estado.processado = false;
  estado.erro = null;
  estado.estagios = [];
  estado.segundos = 0;
  estado.resultado = null;
  estado.clientes = [];
  estado.cliente = null;
  estado.ultimaExportacao = null;
  avisar();

  try {
    await api.processar(parametros);
  } catch (erro) {
    estado.rodando = false;
    estado.erro = erro instanceof Error ? erro.message : String(erro);
    avisar();
    return;
  }

  acompanhar(
    (estagio, segundos) => {
      estado.estagios.push(estagio);
      estado.segundos = segundos;
      // Só a aba Base mostra o progresso; redesenhar outra seção é desperdício.
      if (estado.secao === 'base' || estado.secao === 'processamento') avisar();
    },
    async (erro, segundos) => {
      estado.rodando = false;
      estado.segundos = segundos;
      if (erro) {
        estado.erro = erro;
        avisar();
        return;
      }
      await carregarResultado();
    },
  );
}

async function carregarResultado() {
  try {
    estado.resultado = await api.resultado();
    estado.processado = true;
    estado.erro = null;
    if (estado.resultado.validacao.is_valid) {
      estado.clientes = (await api.clientes()).clientes;
      if (!estado.destinoExportacao) {
        // Sugere a pasta da própria base como destino.
        const caminho = String(estado.resultado.caminho);
        const corte = Math.max(caminho.lastIndexOf('\\'), caminho.lastIndexOf('/'));
        estado.destinoExportacao = corte > 0 ? caminho.slice(0, corte) : '';
      }
      estado.secao = 'processamento';
    } else {
      estado.clientes = [];
      estado.secao = 'validacao';
    }
  } catch (erro) {
    estado.erro = erro instanceof Error ? erro.message : String(erro);
    estado.processado = false;
  }
  avisar();
}

async function encerrar() {
  if (!confirm('Encerrar o Luna? O servidor local será desligado.')) return;
  try {
    await api.encerrar();
  } catch {
    // o servidor desliga antes de responder em alguns casos; nada a fazer
  }
  document.body.replaceChildren(el('div', { classe: 'workspace' }, [
    el('div', { classe: 'pagina' }, [
      el('h1', { texto: 'Luna encerrada' }),
      el('p', { classe: 'linha-apoio',
        texto: 'O servidor local foi desligado. Pode fechar esta janela.' }),
    ]),
  ]));
}

assinar(() => {
  desenharTopbar();
  desenharSidebar();
  desenharWorkspace();
});

// Atalhos: numeros 1..6 trocam de secao.
document.addEventListener('keydown', (evento) => {
  const alvo = /** @type {HTMLElement} */ (evento.target);
  if (['INPUT', 'SELECT', 'TEXTAREA'].includes(alvo?.tagName)) return;
  const indice = Number(evento.key) - 1;
  if (indice >= 0 && indice < SECOES.length) {
    const secao = SECOES[indice];
    if (secao.id === 'base' || estado.processado) irPara(secao.id);
  }
});

async function iniciar() {
  try {
    estado.opcoes = await api.opcoes();
  } catch (erro) {
    workspace.replaceChildren(el('div', { classe: 'pagina' }, [
      el('h1', { texto: 'Luna' }),
      el('p', { classe: 'linha-apoio',
        texto: `Não foi possível falar com o servidor: ${
          erro instanceof Error ? erro.message : erro}` }),
    ]));
    return;
  }

  aplicarTema(estado.opcoes.tema);
  document.title = estado.opcoes.titulo || 'Luna';
  estado.formulario = { ...estado.opcoes.padrao };

  // Uma instancia que ja processou (atalho reaberto) devolve o resultado.
  const atual = await api.estado();
  if (atual.processado) {
    estado.formulario = { ...atual.parametros };
    estado.segundos = atual.segundos || 0;
    await carregarResultado();
    return;
  }
  if (atual.rodando) {
    estado.rodando = true;
    estado.formulario = { ...atual.parametros };
    estado.estagios = atual.estagios || [];
    avisar();
    acompanhar(
      (estagio, segundos) => {
        estado.estagios.push(estagio);
        estado.segundos = segundos;
        avisar();
      },
      async (erro, segundos) => {
        estado.rodando = false;
        estado.segundos = segundos;
        if (erro) { estado.erro = erro; avisar(); return; }
        await carregarResultado();
      },
    );
    return;
  }

  avisar();
}

iniciar();
