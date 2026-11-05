import { execFile } from "node:child_process";

/**
 * Executa um comando externo sem shell (argumentos nunca são interpretados). No Windows, scripts .cmd
 * (como o az.cmd) só rodam via shell — nesse caso os argumentos já chegam validados por quem chama
 * (assertSafeAzureName/assertGmudTarget) e são passados entre aspas.
 */
export function run(command, args, { cwd, timeoutMs = 120_000, env } = {}) {
  const needsShell = process.platform === "win32" && /\.(cmd|bat)$/i.test(command);
  const finalArgs = needsShell ? args.map((a) => `"${String(a).replace(/"/g, '""')}"`) : args;

  return new Promise((resolve) => {
    execFile(
      command,
      finalArgs,
      { cwd, timeout: timeoutMs, maxBuffer: 16 * 1024 * 1024, windowsHide: true, shell: needsShell, env: env ?? process.env },
      (error, stdout, stderr) => {
        resolve({
          exitCode: error ? (typeof error.code === "number" ? error.code : 1) : 0,
          timedOut: Boolean(error?.killed),
          stdout: stdout?.toString() ?? "",
          stderr: stderr?.toString() || (error && typeof error.code !== "number" ? error.message : "")
        });
      }
    ).stdin?.end();
  });
}

export const AZ = process.platform === "win32" ? "az.cmd" : "az";
