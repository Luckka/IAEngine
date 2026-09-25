# OnlineOS Compatibility Surface

## Objetivo

Este documento delimita a compatibilidade histórica que permanece na IAEngine
após o M10. A compatibilidade é opcional e não deve ser inferida por um
consumidor genérico.

## Seleção explícita

O caminho genérico usa:

```text
appsettings.json
```

Esse arquivo declara o perfil `iaengine-generic`, sem providers ou
capabilities. Um comando diferente de `help` falha de forma clara se não houver
host registration.

O caminho legado usa explicitamente:

```text
ONLINEOS_ORCHESTRATOR_CONFIG=/caminho/appsettings.onlineos.json
```

O arquivo legado contém o perfil `onlineos-mobile`, Flutter, Patrol, ADB,
providers históricos e a política de referência `b1208`. Ele continua sendo
compatibilidade do CLI e não configuração default do Core.

## Componentes

| Componente | Papel | Status |
|---|---|---|
| `IAEngine.Core` | Workflow genérico | Independente do adapter |
| `IAEngine.OnlineOSAdapter` | Agents, QA, Flutter, Patrol, ADB e referência | Opcional |
| `OnlineOs.AiOrchestrator` | CLI e composição histórica | Compatibilidade |
| `InfraSentinel` | Consumidor genérico | Core somente |

## Regras de boundary

- O Core não referencia o adapter.
- O InfraSentinel não referencia o adapter.
- O adapter referencia o Core.
- O CLI pode referenciar o adapter somente na composição legada.
- Configuração genérica não instancia providers.
- Configuração ausente ou inválida falha antes da composição.

## Testes

Os testes são divididos em três projetos. Testes de agents, Flutter, Patrol,
ADB, prototype e inspeção de referência pertencem ao adapter. Testes de host,
roadmap, process runner, workspace e boundary pertencem ao Core. Testes que
validam a configuração histórica, o CLI ou modelos ainda legados permanecem na
suíte de compatibilidade.

## Decisões adiadas

Não foram removidos namespaces `OnlineOs.AiOrchestrator`, opções históricas,
campos de prototype ou categorias Flutter/ADB/Patrol do Core. Essas remoções
seriam breaking changes e devem ser tratadas em uma decisão de API major.
