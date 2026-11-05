import { devops, enc, scope, DEFAULT_ORG, DEFAULT_REPOS_PROJECT } from "../azure-devops.js";

// Somente leitura: criar branch, merge e push ficam no gmud-automation (gmud run), que aplica os
// limites de escrita (sem --force, sem sobrescrever release, confirmação explícita).

const reposProps = {
  organization: { type: "string", description: `Organização do Azure DevOps (padrão: ${DEFAULT_ORG})` },
  reposProject: { type: "string", description: `Projeto dos repositórios/pipelines (padrão: ${DEFAULT_REPOS_PROJECT})` }
};

const repoPath = (s, repository, rest) => `${enc(s.reposProject)}/_apis/git/repositories/${enc(repository)}/${rest}`;

async function getRef(s, repository, branch) {
  const data = await devops(s.organization, repoPath(s, repository, `refs?filter=${enc("heads/" + branch)}`), { allowNotFound: true });
  // O filtro é por prefixo: compara o nome exato.
  return data?.value?.find((r) => r.name.toLowerCase() === `refs/heads/${branch}`.toLowerCase()) ?? null;
}

export const reposTools = [
  {
    name: "list_repositories",
    description: "Lista os repositórios Git do projeto (nome e ID — o ID aparece nos links de commit/PR/branch dos cards).",
    readOnly: true,
    inputSchema: { type: "object", properties: { ...reposProps } },
    async handler(args) {
      const s = scope(args);
      const data = await devops(s.organization, `${enc(s.reposProject)}/_apis/git/repositories`);
      return data.value.map((r) => ({ id: r.id, name: r.name, defaultBranch: r.defaultBranch }));
    }
  },
  {
    name: "list_branches",
    description: "Lista branches de um repositório pelo prefixo (ex: 'release/GMUD_PROD_CLI1_', 'feature/us_20611') com o commit de cada uma.",
    readOnly: true,
    inputSchema: {
      type: "object",
      properties: {
        repository: { type: "string", description: "Nome do repositório" },
        prefix: { type: "string", description: "Prefixo do nome da branch, sem refs/heads/ (padrão: todas)" },
        ...reposProps
      },
      required: ["repository"]
    },
    async handler(args) {
      const s = scope(args);
      const filter = args.prefix ? `?filter=${enc("heads/" + args.prefix)}` : "?filter=heads/";
      const data = await devops(s.organization, repoPath(s, args.repository, `refs${filter}`), { allowNotFound: true });
      if (!data) throw new Error(`Repositório "${args.repository}" não encontrado em ${s.reposProject}.`);
      return data.value.map((r) => ({ name: r.name.replace("refs/heads/", ""), objectId: r.objectId }));
    }
  },
  {
    name: "compare_branches",
    description:
      "Compara duas branches: commits que 'target' tem e 'base' não tem (mensagem e se é merge) e arquivos que diferem desde o ancestral comum. " +
      "Use para revisar o escopo de uma feature contra a release de produção antes de uma GMUD.",
    readOnly: true,
    inputSchema: {
      type: "object",
      properties: {
        repository: { type: "string" },
        base: { type: "string", description: "Branch base (ex: release/GMUD_PROD_CLI1_22102026)" },
        target: { type: "string", description: "Branch comparada (ex: feature/us_20611)" },
        ...reposProps
      },
      required: ["repository", "base", "target"]
    },
    async handler(args) {
      const s = scope(args);
      const [base, target] = await Promise.all([getRef(s, args.repository, args.base), getRef(s, args.repository, args.target)]);
      if (!base) throw new Error(`Branch "${args.base}" não existe em ${args.repository}.`);
      if (!target) throw new Error(`Branch "${args.target}" não existe em ${args.repository}.`);

      const commits = await devops(s.organization, repoPath(s, args.repository, "commitsbatch?$top=500"), {
        method: "POST",
        body: { itemVersion: { version: target.objectId, versionType: "commit" }, compareVersion: { version: base.objectId, versionType: "commit" } }
      });
      const diff = await devops(
        s.organization,
        repoPath(s, args.repository, `diffs/commits?baseVersion=${base.objectId}&baseVersionType=commit&targetVersion=${target.objectId}&targetVersionType=commit&$top=2000`)
      );

      return {
        base: { branch: args.base, commit: base.objectId },
        target: { branch: args.target, commit: target.objectId },
        commonCommit: diff.commonCommit,
        commitsAhead: commits.value.map((c) => ({
          commitId: c.commitId,
          author: c.author?.name,
          date: c.author?.date,
          isMerge: (c.parents?.length ?? 0) > 1,
          comment: c.comment
        })),
        files: (diff.changes ?? []).filter((c) => !c.item?.isFolder).map((c) => ({ path: c.item.path, changeType: c.changeType })),
        allChangesIncluded: diff.allChangesIncluded
      };
    }
  },
  {
    name: "list_builds",
    description: "Lista os builds de uma branch (mais recentes primeiro): ID (prefixo da tag do container), status, resultado, pipeline e commit.",
    readOnly: true,
    inputSchema: {
      type: "object",
      properties: {
        branch: { type: "string", description: "Branch sem refs/heads/ (ex: release/GMUD_PROD_CLI1_22102026)" },
        top: { type: "number", description: "Quantidade (padrão 10, máx. 50)" },
        ...reposProps
      },
      required: ["branch"]
    },
    async handler(args) {
      const s = scope(args);
      const top = Math.min(Math.max(Number(args.top) || 10, 1), 50);
      const data = await devops(
        s.organization,
        `${enc(s.reposProject)}/_apis/build/builds?branchName=${enc("refs/heads/" + args.branch)}&$top=${top}&queryOrder=queueTimeDescending`
      );
      return data.value.map((b) => ({
        id: b.id,
        buildNumber: b.buildNumber,
        status: b.status,
        result: b.result,
        pipeline: b.definition?.name,
        sourceVersion: b.sourceVersion,
        queueTime: b.queueTime,
        finishTime: b.finishTime,
        url: b._links?.web?.href
      }));
    }
  }
];
