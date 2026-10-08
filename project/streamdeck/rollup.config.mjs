import { join } from "node:path";

import commonjs from "@rollup/plugin-commonjs";
import { nodeResolve } from "@rollup/plugin-node-resolve";
import typescript from "@rollup/plugin-typescript";

export default {
  input: "src/plugin.ts",
  output: {
    file: join(process.env.PLUGIN_DIR, "bin/plugin.js"),
    format: "esm",
    sourcemap: true
  },
  plugins: [nodeResolve(), commonjs(), typescript()]
};
