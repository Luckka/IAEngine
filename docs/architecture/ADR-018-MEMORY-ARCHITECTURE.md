# ADR-018 — Arquitetura de memória e Rewind

Status: accepted for M21  
Data: 2026-09-30

## Contexto

Agentes precisam recuperar decisões e eventos de sessões anteriores com evidência, sem depender apenas da janela atual. A fonte precisa ser auditável, local e reconstruível, e Rewind não pode virar recovery.

## Decisão

Manter contratos e modelos genéricos no `IAEngine.Core.Memory`, sem SQLite, adapter, OnlineOS ou InfraSentinel. Implementar SQLite em `IAEngine.Memory.Local` com cinco tabelas (`memory_events`, `memory_records`, `memory_citations`, `memory_checkpoints`, `memory_projections`), índices determinísticos e redaction pré-persistência.

Eventos são append-only e têm id independente de timestamp. Records e projeções são derivados, citam o evento de origem e podem ser reconstruídos. Retrieval é lexical e explicável; context builder aplica limites. Rewind é uma projeção de leitura limitada ao checkpoint.

O `EngineHost` aceita `IMemoryEventSink` e `IMemoryContextBuilder` opcionalmente. Sem esses componentes o comportamento legado permanece inalterado.

## Consequências

Há maior rastreabilidade, isolamento entre projetos/runs e testes locais sem serviços externos. Busca lexical não resolve semântica implícita; embeddings ficam deliberadamente fora do milestone. A dependência SQLite requer acompanhamento do alerta transitivo do NuGet.

## Alternativas rejeitadas

Vector database, embeddings, MCP e provedores externos foram rejeitados por escopo e por dificultarem determinismo/offline. Persistir apenas resumos foi rejeitado porque perde a fonte de verdade e impede auditoria.
