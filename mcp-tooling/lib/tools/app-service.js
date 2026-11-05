import { AZ, run } from "../process.js";
import { assertSafeAzureName, parseContainerImage } from "../parsers.js";

// Azure CLI só para App Services (regra do fluxo de GMUD), sempre leitura: a troca de tag é da infra.

function subscriptionArgs(args) {
  const subscription = args.subscription || process.env.AZURE_SUBSCRIPTION_ID;
  if (!subscription) return [];
  if (!/^[0-9a-fA-F-]{36}$/.test(subscription)) throw new Error("subscription deve ser o GUID da assinatura.");
  return ["--subscription", subscription];
}

async function az(args) {
  const result = await run(AZ, [...args, "--only-show-errors", "-o", "json"], { timeoutMs: 90_000 });
  if (result.exitCode !== 0) {
    const detail = result.stderr.trim() || result.stdout.trim();
    throw new Error(
      /az login|Please run 'az login'|AADSTS/i.test(detail)
        ? "Azure CLI sem sessão válida — rode 'az login' no terminal."
        : `Azure CLI falhou (${result.exitCode}): ${detail.slice(0, 500)}`
    );
  }
  return result.stdout.trim() ? JSON.parse(result.stdout) : null;
}

const subscriptionProp = { subscription: { type: "string", description: "GUID da assinatura (padrão: AZURE_SUBSCRIPTION_ID)" } };

export const appServiceTools = [
  {
    name: "find_app_services",
    description: "Lista os App Services cujo nome contém o fragmento (ex: o cliente) — use quando o nome padrão do site não existir.",
    readOnly: true,
    inputSchema: { type: "object", properties: { fragment: { type: "string" }, ...subscriptionProp }, required: ["fragment"] },
    async handler(args) {
      const fragment = assertSafeAzureName(args.fragment, "fragment").toLowerCase();
      const sites = await az(["webapp", "list", "--query", `[?contains(name, '${fragment}')].{name:name, resourceGroup:resourceGroup, state:state}`, ...subscriptionArgs(args)]);
      return sites ?? [];
    }
  },
  {
    name: "get_app_service_image",
    description:
      "Lê a imagem de container configurada num App Service (linuxFxVersion) e decompõe a tag em buildId, release (branch de origem) e commitSha.",
    readOnly: true,
    inputSchema: {
      type: "object",
      properties: {
        site: { type: "string", description: "Nome do App Service (ex: contoso-portalcliente-web-prd-cli1)" },
        resourceGroup: { type: "string", description: "Resource group (opcional: descoberto pelo nome)" },
        ...subscriptionProp
      },
      required: ["site"]
    },
    async handler(args) {
      const site = assertSafeAzureName(args.site, "site");
      let resourceGroup = args.resourceGroup ? assertSafeAzureName(args.resourceGroup, "resourceGroup") : null;
      if (!resourceGroup) {
        const found = await az(["webapp", "list", "--query", `[?name=='${site}'].resourceGroup`, ...subscriptionArgs(args)]);
        resourceGroup = found?.[0];
        if (!resourceGroup) throw new Error(`App Service "${site}" não encontrado. Use find_app_services para listar candidatos.`);
      }

      const linuxFxVersion = await az(["webapp", "config", "show", "--name", site, "--resource-group", resourceGroup, "--query", "linuxFxVersion", ...subscriptionArgs(args)]);
      return { site, resourceGroup, linuxFxVersion, parsed: parseContainerImage(linuxFxVersion) };
    }
  },
  {
    name: "parse_container_tag",
    description: "Decompõe uma imagem/tag '<registro>/<projeto>:<buildId>-<release>-<commitSha>' em buildId, release, branch de origem e commitSha (sem chamar o Azure).",
    readOnly: true,
    inputSchema: { type: "object", properties: { image: { type: "string" } }, required: ["image"] },
    async handler(args) {
      return parseContainerImage(args.image) ?? { error: "Imagem sem tag." };
    }
  }
];
