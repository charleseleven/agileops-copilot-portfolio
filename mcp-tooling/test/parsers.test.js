import { test } from "node:test";
import assert from "node:assert/strict";
import { assertGmudTarget, assertSafeAzureName, extractWorkItemReferences, parseArtifactLink, parseContainerImage } from "../lib/parsers.js";

const sha = "6618eda39fb8daf10b122ac0b76d9fcec8629a78";

test("parseContainerImage decompõe buildId, release e commit", () => {
  const parsed = parseContainerImage(`DOCKER|registro.azurecr.io/contoso.portalcliente.web:30945-GMUD_PROD_CLI1_22102026-${sha}`);
  assert.equal(parsed.repository, "registro.azurecr.io/contoso.portalcliente.web");
  assert.equal(parsed.buildId, 30945);
  assert.equal(parsed.release, "GMUD_PROD_CLI1_22102026");
  assert.equal(parsed.sourceBranch, "release/GMUD_PROD_CLI1_22102026");
  assert.equal(parsed.commitSha, sha);
});

test("parseContainerImage aceita só a tag e mantém main_<cliente> sem prefixo", () => {
  const sha2 = "7432b205df6ce05a52c0321da5b0559d3bc76076";
  const release = parseContainerImage(`31229-GMUD_PROD_CLI1_29102026-${sha2}`);
  assert.equal(release.buildId, 31229);
  assert.equal(release.sourceBranch, "release/GMUD_PROD_CLI1_29102026");
  assert.equal(release.commitSha, sha2);

  const main = parseContainerImage(`registro.azurecr.io/contoso.portalcliente.api:31230-main_cli1-${sha2}`);
  assert.equal(main.release, "main_cli1");
  assert.equal(main.sourceBranch, "main_cli1");
  assert.equal(parseContainerImage(`1-mainline-${sha2}`).sourceBranch, "release/mainline");
});

test("parseContainerImage com tag fora do padrão retorna partes nulas", () => {
  assert.equal(parseContainerImage("registro/app:latest").buildId, null);
  assert.equal(parseContainerImage("registro/app"), null);
  assert.equal(parseContainerImage(null), null);
});

test("parseArtifactLink decodifica commit e branch", () => {
  assert.deepEqual(parseArtifactLink("vstfs:///Git/Commit/p%2Fr%2Fabc"), { kind: "commit", projectId: "p", repositoryId: "r", value: "abc" });
  assert.equal(parseArtifactLink("vstfs:///Git/Ref/p%2Fr%2FGBfeature%2Fus_20611").value, "feature/us_20611");
  assert.equal(parseArtifactLink("vstfs:///Git/Ref/p%2Fr%2FGTv1"), null);
  assert.equal(parseArtifactLink("vstfs:///Build/Build/1"), null);
});

test("extractWorkItemReferences pega links e #id na ordem, sem cores CSS", () => {
  const html = '<p style="color:#fff">Aplicar <a href="https://dev.azure.com/x/y/_workitems/edit/20611/">#20611</a> e #20700</p>';
  assert.deepEqual(extractWorkItemReferences(html), [20611, 20700]);
});

test("assertSafeAzureName bloqueia caracteres de shell", () => {
  assert.equal(assertSafeAzureName("contoso-web-prd-cli1", "site"), "contoso-web-prd-cli1");
  for (const bad of ["a&b", "x\"y", "a b", "$(x)", "", "a|b"]) {
    assert.throws(() => assertSafeAzureName(bad, "site"));
  }
});

test("assertGmudTarget aceita ID e link do Boards e recusa o resto", () => {
  assert.equal(assertGmudTarget("25482"), "25482");
  assert.equal(assertGmudTarget("#25482"), "25482");
  assert.equal(assertGmudTarget("https://dev.azure.com/contoso/Contoso%20Projetos/_workitems/edit/25482/"),
    "https://dev.azure.com/contoso/Contoso%20Projetos/_workitems/edit/25482/");
  for (const bad of ["25482; rm -rf", "https://evil.com/_workitems/edit/1", "abc", ""]) {
    assert.throws(() => assertGmudTarget(bad));
  }
});
