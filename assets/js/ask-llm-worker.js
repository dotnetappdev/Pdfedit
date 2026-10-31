// Runs the free in-browser AI for "Ask the guide" (WebLLM, on the GPU through WebGPU) in a worker,
// so the page stays responsive while the model loads and writes.
import { WebWorkerMLCEngineHandler } from 'https://cdn.jsdelivr.net/npm/@mlc-ai/web-llm@0.2.84/+esm';

const handler = new WebWorkerMLCEngineHandler();
self.onmessage = message => handler.onmessage(message);
