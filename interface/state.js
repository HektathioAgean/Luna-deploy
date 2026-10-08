// @ts-check
// Estado unico da interface. Quem muda chama avisar(); quem desenha assina.

/**
 * @typedef {{caminho:string, tempo_min_expurgo:number, tempo_max_anomalia:number,
 *   eventos_previos:number, minimo_apontamentos:number,
 *   tempo_padrao_poucos_apontamentos:number, ajuste_percentual:number,
 *   exibir_janelas_entrega:boolean, cobertura_janela:number,
 *   base_janela:string}} Parametros
 * @typedef {'base'|'validacao'|'processamento'|'cliente'|'resultados'|'exportacao'} Secao
 */

export const SECOES = /** @type {{id:Secao, rotulo:string, icone:string}[]} */ ([
  { id: 'base', rotulo: 'Base', icone: 'ic-arquivo' },
  { id: 'validacao', rotulo: 'Validação', icone: 'ic-camadas' },
  { id: 'processamento', rotulo: 'Processamento', icone: 'ic-painel' },
  { id: 'cliente', rotulo: 'Painel do Cliente', icone: 'ic-pessoa' },
  { id: 'resultados', rotulo: 'Resultados', icone: 'ic-tabela' },
  { id: 'exportacao', rotulo: 'Exportação', icone: 'ic-baixar' },
]);

export const estado = {
  /** @type {any} resposta de /api/opcoes */
  opcoes: null,
  /** @type {Parametros|null} valores atuais do formulario */
  formulario: null,
  /** @type {Secao} */
  secao: 'base',

  rodando: false,
  processado: false,
  /** @type {string|null} */
  erro: null,
  /** @type {string[]} estagios ja vistos nesta execucao */
  estagios: [],
  segundos: 0,

  /** @type {any} resposta de /api/resultado */
  resultado: null,
  /** @type {string[]} */
  clientes: [],

  // Painel do cliente
  /** @type {string|null} */
  cliente: null,
  diaSemana: 'TODOS',
  rotulosTempo: true,

  // Navegador de arquivos
  navegadorAberto: false,
  /** @type {any} resposta de /api/procurar */
  navegador: null,

  // Exportacao
  destinoExportacao: '',
  /** @type {string|null} */
  ultimaExportacao: null,
};

/** @type {((e: typeof estado) => void)[]} */
const ouvintes = [];

/** @param {(e: typeof estado) => void} fn */
export function assinar(fn) {
  ouvintes.push(fn);
}

export function avisar() {
  ouvintes.forEach((fn) => fn(estado));
}

/** Os parametros ja processados; antes disso, o que esta no formulario. */
export function parametrosEmUso() {
  return estado.resultado?.parametros || estado.formulario;
}

/** True quando o formulario difere do que foi processado. */
export function formularioMudou() {
  if (!estado.processado || !estado.resultado || !estado.formulario) return false;
  const processado = estado.resultado.parametros;
  return Object.keys(estado.formulario).some(
    (chave) => String(estado.formulario[chave]) !== String(processado[chave])
  );
}
