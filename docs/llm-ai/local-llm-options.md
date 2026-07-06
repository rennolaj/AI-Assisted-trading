# Local LLM Options (Design Notes)

## Provider Switch
The MCP gateway supports provider selection via configuration:
- `McpProvider:Provider`: `openai`, `local`, or `auto`
- `McpProvider:FallbackOnOpenAi429`: if `true`, `auto` will fall back on OpenAI 429s
- `LocalLlm:BaseUrl`: local endpoint base URL
- `LocalLlm:ApiKey`: optional
- `LocalLlm:ResponsesPath`: endpoint path (default `responses`)
- `LocalLlm:ModelOverride`: optional model override
- `LocalLlm:ChatCompletionsPath`: endpoint path (default `chat/completions`)
- `LocalLlm:Mode`: `responses` or `chat`
- `LocalLlm:UseResponseFormat`: include response_format for chat (if supported)

## Local Runtime Options
Common downloadable runtimes that can host local models:
- Ollama (model manager + server)
- LM Studio (GUI + local server)
- LocalAI (OpenAI-compatible server)
- llama.cpp server
- vLLM (GPU-optimized server)
- GPT4All

Note: Most local servers expose OpenAI-compatible **chat/completions** endpoints. Set `LocalLlm:Mode=chat` for those runtimes.
LM Studio defaults to `http://localhost:1234/v1/` and works best with chat mode.

## Model Families (Examples)
From the Ollama library, common local-ready families include:
- Llama 3.x (meta-llama)
- Mistral / Mixtral
- Gemma 2
- Qwen 2.x
- Phi-3 / Phi-4
- DeepSeek (general + code)
- Code Llama / Codestral

Pick a model that supports JSON/schema-constrained output and fits available CPU/GPU resources.
