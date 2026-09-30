# Runbook — Memory e Rewind

## Verificação

Use uma base local dedicada por projeto/run. Confirme que o processo está offline, que o caminho não é versionado e que não há secrets no payload. Reabra a base com `SqliteMemoryStore`, consulte eventos por projeto e execute `RebuildProjectionsAsync(projectId)`. Compare a contagem de eventos antes/depois; a reconstrução não deve alterar `memory_events`.

## Busca e auditoria

Uma resposta auditável deve guardar a pergunta, `MemoryId`, `SourceEventId`, timestamp, projeto, milestone/task, score, citações, limitações, conflitos e checkpoint. Sem evidência suficiente, retorne `MEMORY_NOT_FOUND`; com decisões incompatíveis, `MEMORY_CONFLICT` e a evolução `Supersedes/SupersededBy`.

## Rewind

Selecione um checkpoint conhecido e chame `RewindAsync(projectId, checkpointId, query)`. Valide que eventos posteriores não estão no contexto, que o log continua com a mesma contagem e que nenhuma task/commit foi executada. Rewind não é recuperação de execução.

## Corrupção e recuperação

Isole o arquivo, copie-o para preservação, não tente editar o log manualmente e reabra uma cópia. Se SQLite não abrir, restaure o último backup local aprovado; a perda deve ser explicitamente reportada. Projeções podem ser apagadas/reconstruídas, mas o event log é a fonte de verdade.

## Exportação e eliminação

Exporte somente records e citações necessários, aplique redaction novamente e mantenha a origem. Para apagar dados, faça uma operação explícita, autorizada e auditada no arquivo local; não use Rewind nem altere eventos históricos silenciosamente.
