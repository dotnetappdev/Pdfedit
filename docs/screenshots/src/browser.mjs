import path from 'path';
import { createRequire } from 'module';
import { execSync } from 'child_process';
// Playwright from the project, or the globally installed copy.
const require = createRequire(import.meta.url);
let pw;
try { pw = require('playwright'); } catch { pw = require(path.join(execSync('npm root -g').toString().trim(), 'playwright')); }
export const { chromium } = pw;
