# M21 — IAEngine Memory and Rewind

## Problema e escopo

Memória é contexto histórico persistido; não é a janela de contexto do modelo e não é recovery de execução. M21 registra eventos append-only, reconstrói projeções e recupera contexto com proveniência. Não executa tarefas, não altera o log e não recupera uma execução interrompida.

O Core expõe contratos neutros em `IAEngine.Core.Memory`. A implementação local está em `IAEngine.Memory.Local` e usa SQLite, sem rede, AWS, MCP, embeddings ou vector database.

## Modelo

- Working: estado da execução atual.
- Episodic: fatos observáveis, como task, validação ou falha.
- Semantic: conhecimento/decisões consolidados.
- Procedural: instruções e padrões reutilizáveis.

`MemoryEvent` é a fonte de verdade. `MemoryRecord`, citações e projeções são derivados e podem ser reconstruídos. Todo record aponta para `SourceEventId` e mantém projeto, run, milestone, task, status, confiança, timestamps e relações de substituição.

## Fluxo e recuperação

O fluxo é `event sink → SQLite event log → projection → lexical retrieval → context builder`. A busca usa filtros por texto, projeto, milestone, task, tipo, status, período e origem, com ordenação estável e score explicável. O resultado inclui memória, evento de origem, citação, limitações e conflitos. Ausência de resultados deve ser tratada pelo consumidor como `MEMORY_NOT_FOUND`; memória conflitante como `MEMORY_CONFLICT`.

O builder aplica limites de records, caracteres, eventos e citações. A prioridade é: decisões aprovadas, fatos atuais, tarefas abertas, falhas recentes e histórico antigo. Não injeta o histórico inteiro automaticamente.

## Proveniência, redaction e privacidade

Redaction acontece antes do INSERT e substitui chaves/valores de tokens, credenciais, headers, cookies, senhas, connection strings e identificadores sensíveis. Payload inválido que corresponda a padrão sensível não é persistido. A sanitização é determinística. O banco é local; arquivos `*.db`, `*.sqlite` e `*.sqlite3` estão ignorados.

Não há exclusão implícita nem sobrescrita silenciosa. Para apagar memória, pare consumidores, faça backup se necessário, remova explicitamente o arquivo local ou registros autorizados e registre a operação fora do log de memória. Nunca use apagamento para simular Rewind.

## Conflitos e Rewind

Decisões novas podem informar `Supersedes`; a decisão anterior permanece visível como `Superseded`. O consumidor deve exibir a decisão atual e a evolução sem combinar decisões incompatíveis.

`RewindAsync(projectId, checkpointId, query)` filtra eventos até o timestamp do checkpoint, reconstrói os records relacionados e produz `MemoryContext`. É uma leitura: não apaga eventos, não modifica o estado real, não cria commits e não executa tasks.

## Integridade, schema e operação futura

O schema atual é versionado nos eventos, checkpoints e projeções (`SchemaVersion = 1`). Mudanças incompatíveis devem adicionar migração explícita e teste de reabertura; nunca alterar silenciosamente o significado de um evento já gravado. Para verificar integridade, reabra o SQLite, valide as tabelas/índices e execute `RebuildProjectionsAsync` comparando a contagem de eventos.

Para exportar uma memória, leia o record e suas citações/evento de origem e remova dados sensíveis novamente antes de compartilhar. Para auditar uma resposta, retenha a pergunta, ids dos records, citações, limitações, conflitos e checkpoint usado.

Embeddings/vector search ficam como decisão futura após validar a busca lexical determinística. A dependência SQLite atual gera um alerta transitivo de segurança no restore; deve ser atualizada ou substituída em trabalho próprio antes de uso distribuído.
