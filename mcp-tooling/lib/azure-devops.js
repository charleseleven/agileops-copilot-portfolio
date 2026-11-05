// Acesso REST ao Azure DevOps compartilhado pelas tools. Organização/projeto têm padrão (evita repetir em
// toda chamada), mas são sempre sobrescrevíveis por chamada — o usuário alterna entre várias organizações.

export const DEFAULT_ORG = process.env.AZURE_DEVOPS_DEFAULT_ORG || "eleven11C";
export const DEFAULT_PROJECT = process.env.AZURE_DEVOPS_DEFAULT_PROJECT || "Applied AI Engineering";

// Boards e Repos podem viver em Team Projects diferentes na mesma organização.
export const DEFAULT_REPOS_PROJECT = process.env.AZURE_DEVOPS_DEFAULT_REPOS_PROJECT || DEFAULT_PROJECT;

const API_VERSION = "7.1";

// PAT por organização: a organização padrão usa AZURE_DEVOPS_EXT_PAT (mesmo nome da extensão azure-devops
// da Azure CLI); qualquer outra usa AZURE_DEVOPS_PAT_<ORGANIZACAO_MAIUSCULA>, já que um PAT vale para uma só organização.
export function patVariableFor(organization) {
  return organization === DEFAULT_ORG
    ? "AZURE_DEVOPS_EXT_PAT"
    : `AZURE_DEVOPS_PAT_${organization.toUpperCase().replace(/[^A-Z0-9]/g, "_")}`;
}

function resolvePat(organization) {
  const variable = patVariableFor(organization);
  const pat = process.env[variable];
  if (!pat) {
    throw new Error(
      `Nenhum PAT configurado para a organização "${organization}". Defina a variável ${variable} em mcp-tooling/.env.`
    );
  }
  return pat;
}

function authHeaders(organization) {
  return { Authorization: "Basic " + Buffer.from(":" + resolvePat(organization)).toString("base64") };
}

/** Resolve organização e projetos de uma chamada a partir dos argumentos da tool. */
export function scope(args = {}) {
  return {
    organization: args.organization || DEFAULT_ORG,
    project: args.project || DEFAULT_PROJECT,
    reposProject: args.reposProject || args.project || DEFAULT_REPOS_PROJECT
  };
}

function withApiVersion(path, version = API_VERSION) {
  return path + (path.includes("?") ? "&" : "?") + `api-version=${version}`;
}

/**
 * Chamada REST. `path` é relativo a https://dev.azure.com/<org>/. Repassa a mensagem de erro real do Azure
 * (ex: transição de estado inválida) em vez de mascará-la; 404 vira null quando `allowNotFound`.
 */
export async function devops(organization, path, { method = "GET", body, contentType = "application/json", apiVersion, allowNotFound = false, raw = false } = {}) {
  const url = `https://dev.azure.com/${encodeURIComponent(organization)}/${withApiVersion(path, apiVersion)}`;
  const response = await fetch(url, {
    method,
    headers: { ...authHeaders(organization), ...(body ? { "Content-Type": contentType } : {}) },
    body: body === undefined ? undefined : typeof body === "string" ? body : JSON.stringify(body)
  });

  if (allowNotFound && response.status === 404) {
    return null;
  }

  if (raw && response.ok) {
    return response;
  }

  const text = await response.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = null;
  }

  if (!response.ok) {
    const detail = json?.message || text || "(corpo de resposta vazio)";
    throw new Error(`Azure DevOps API retornou ${response.status} em ${method} ${path.split("?")[0]}: ${detail}`);
  }

  return json;
}

export const enc = encodeURIComponent;
