// =============================================================================
//  ForZip — arnés de prueba del abridor HTML de sobres .fzs
// =============================================================================
//
//  Copyright (c) 2026 Claudio Andino — Patagonia Robot
//  Licensed under the Apache License, Version 2.0
//
// =============================================================================
//
//  Corre el núcleo de Tools/Open_FZS.html fuera del navegador, con la Web Crypto
//  de Node, que es la misma API que usa la página. El núcleo se toma del HTML tal cual está
//  —no hay una copia aparte que pueda quedar desincronizada—, así que lo que se prueba acá
//  es exactamente lo que ejecuta el navegador.
//
//  Uso: node abridor_harness.mjs <Open_FZS.html> <sobre.fzs> <salida.zip>
//       La contraseña entra por la entrada estándar.
//
//  Códigos de salida (los mismos que fzs_extract.py):
//       0 abierto · 1 uso o formato · 2 contraseña incorrecta · 3 truncado o alterado

import { readFileSync, openAsBlob, openSync, writeSync, closeSync, renameSync, rmSync } from "node:fs";
import vm from "node:vm";

const EXIT = { formato: 1, version: 1, algoritmo: 1, contrasena: 2, truncado: 3, sobrante: 3, alterado: 3, cancelado: 1 };

async function readPassword() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString("utf8").replace(/\r?\n$/, "");
}

function loadCore(htmlPath) {
  const html = readFileSync(htmlPath, "utf8");
  const match = html.match(/<script id="fzs-nucleo">([\s\S]*?)<\/script>/);
  if (!match) {
    throw new Error("El HTML no tiene el bloque <script id=\"fzs-nucleo\">.");
  }
  vm.runInThisContext(match[1] + "\n;globalThis.FzsNucleo = FzsNucleo;", { filename: "fzs-nucleo.js" });
  return globalThis.FzsNucleo;
}

async function main() {
  const [htmlPath, sealedPath, outputPath] = process.argv.slice(2);
  if (!htmlPath || !sealedPath || !outputPath) {
    console.error("Uso: node abridor_harness.mjs <html> <sobre.fzs> <salida.zip>");
    return 1;
  }

  const core = loadCore(htmlPath);
  const partial = outputPath + ".partial";
  let fd = null;
  try {
    const blob = await openAsBlob(sealedPath);
    const info = await core.readHeader(blob);
    const key = await core.deriveKey(await readPassword(), info);
    await core.checkKey(blob, info, key);

    fd = openSync(partial, "w");
    await core.open({
      blob,
      info,
      key,
      write: async (bytes) => {
        writeSync(fd, bytes);
      }
    });
    closeSync(fd);
    fd = null;
    renameSync(partial, outputPath);
    console.log(`Abierto: ${info.packageId}`);
    if (process.env.FZS_HARNESS_STATS) {
      // maxRSS viene en KB: es el pico de memoria del proceso en toda la apertura.
      console.log(`Pico de memoria: ${Math.round(process.resourceUsage().maxRSS / 1024)} MB`);
    }
    return 0;
  } catch (error) {
    if (fd !== null) {
      closeSync(fd);
    }
    rmSync(partial, { force: true });
    const code = error && error.name === "FzsError" ? error.code : "interno";
    console.error(`Error: ${code} ${JSON.stringify(error && error.detail ? error.detail : {})} ${error && error.message}`);
    return EXIT[code] ?? 1;
  }
}

process.exitCode = await main();
