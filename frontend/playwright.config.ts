import { existsSync } from 'node:fs';
import { chromium, defineConfig } from '@playwright/test';
export default defineConfig({testDir:'./e2e',fullyParallel:false,workers:1,timeout:30000,use:{baseURL:process.env.MEUBAR_E2E_URL||'http://127.0.0.1:4200',headless:true,launchOptions:{executablePath:process.env.CHROMIUM_PATH||(existsSync(chromium.executablePath())?chromium.executablePath():'/usr/bin/chromium'),args:['--no-sandbox']},screenshot:'only-on-failure'},reporter:'list'});
