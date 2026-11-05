// Funções puras (sem rede) usadas pelas tools — testadas em test/parsers.test.js.
// As regras de negócio da GMUD vivem no gmud-automation (C#); aqui só decodificamos formatos do Azure.

// Mesma regra de GmudNamingConventions.SourceBranchFromRelease (padrão de naming.sourceBranchesWithoutPrefix).
const BRANCHES_WITHOUT_PREFIX = /^main(_.*)?$/i;

/** Branch publicada a partir do segmento central da tag: "GMUD_PROD_X_29102026" → "release/GMUD_PROD_X_29102026"; "main_x" fica como está. */
export function sourceBranchFromRelease(release) {
  return BRANCHES_WITHOUT_PREFIX.test(release) ? release : `release/${release}`;
}

/**
 * Decompõe a imagem do container "registro/projeto:<buildId>-<release>-<commitSha>" (com ou sem "DOCKER|"),
 * ou só a tag: buildId antes do primeiro hífen, commitSha (40 hex) no fim e a release (branch) no meio.
 */
export function parseContainerImage(image) {
  if (!image || typeof image !== "string") return null;
  let value = image.trim();
  const pipe = value.indexOf("|");
  if (pipe >= 0) value = value.slice(pipe + 1);

  const lastSlash = value.lastIndexOf("/");
  const colon = value.indexOf(":", lastSlash + 1);
  const tagPattern = /^(\d+)-(.+)-([0-9a-fA-F]{40})$/;
  if (colon < 0 && !(lastSlash < 0 && tagPattern.test(value))) return null;

  const repository = colon < 0 ? null : value.slice(0, colon);
  const tag = colon < 0 ? value : value.slice(colon + 1);
  const match = tagPattern.exec(tag);
  if (!match) return { image: value, repository, tag, buildId: null, release: null, commitSha: null };

  return {
    image: value,
    repository,
    tag,
    buildId: Number(match[1]),
    release: match[2],
    commitSha: match[3].toLowerCase(),
    sourceBranch: sourceBranchFromRelease(match[2])
  };
}

/** Links de desenvolvimento dos cards: vstfs:///Git/{Commit|PullRequestId|Ref}/{projeto}%2F{repo}%2F{valor}. */
export function parseArtifactLink(uri) {
  if (!uri || !uri.startsWith("vstfs:///Git/")) return null;
  const rest = uri.slice("vstfs:///Git/".length);
  const slash = rest.indexOf("/");
  if (slash <= 0) return null;

  const type = rest.slice(0, slash).toLowerCase();
  const parts = decodeURIComponent(rest.slice(slash + 1)).split("/");
  if (parts.length < 3) return null;
  const [projectId, repositoryId, ...valueParts] = parts;
  let value = valueParts.join("/");

  const kind = { commit: "commit", pullrequestid: "pullRequest", ref: "branch" }[type];
  if (!kind) return null;
  if (kind === "branch") {
    if (!value.startsWith("GB")) return null;
    value = value.slice(2);
  }

  return { kind, projectId, repositoryId, value };
}

/** IDs citados num HTML de descrição: links _workitems/edit/<id> e menções #<id>, na ordem, sem repetir. */
export function extractWorkItemReferences(html) {
  if (!html) return [];
  const ids = [];
  const add = (v) => {
    const id = Number(v);
    if (id > 0 && !ids.includes(id)) ids.push(id);
  };

  const pattern = /\/_workitems\/edit\/(\d+)|(?<![\w&/])#(\d{1,9})\b/gi;
  const text = html.replace(/style="[^"]*"/gi, "");
  for (const match of text.matchAll(pattern)) add(match[1] ?? match[2]);
  return ids;
}

/** Nomes aceitos em comandos externos (App Service, resource group): impede injeção ao chamar o az. */
export function assertSafeAzureName(value, label) {
  if (typeof value !== "string" || !/^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$/.test(value)) {
    throw new Error(`${label} inválido: use apenas letras, números, ".", "_" e "-".`);
  }
  return value;
}

/** Entrada do gmud run: ID numérico ou link https do Azure Boards. */
export function assertGmudTarget(value) {
  const text = String(value ?? "").trim();
  if (/^#?\d{1,9}$/.test(text)) return text.replace("#", "");
  if (/^https:\/\/dev\.azure\.com\/[^\s"'`;|&<>]+\/_workitems\/edit\/\d+\/?$/i.test(text)) return text;
  throw new Error("Informe o ID numérico da GMUD ou o link https://dev.azure.com/<org>/<projeto>/_workitems/edit/<id>.");
}

export const EXIT_CODE_MEANING = {
  0: "ok",
  1: "falha",
  2: "aguardando decisão do operador",
  3: "confirmação de escrita necessária (rode com confirm_write após o usuário aprovar o plano)"
};
