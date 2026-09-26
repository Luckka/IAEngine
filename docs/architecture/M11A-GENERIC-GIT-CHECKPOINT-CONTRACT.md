# M11-A — Generic Git Checkpoint Contract

## Lacuna original

O `EngineHost` já executava tasks, validation, review, remediation e
milestones, mas não possuía uma abstração genérica para decidir ou executar um
checkpoint Git depois de uma execução aprovada. `IGitService` expõe leitura do
estado; ele não representa autorização de commit.

O `GitWorkflowManager` existente não foi reutilizado diretamente. Ele contém
responsabilidades históricas de lifecycle, branches, push e merge que não são
apropriadas para o Core genérico.

## Contrato criado

O namespace neutro `IAEngine.Core.Git` contém:

- `IGitCheckpointCoordinator`;
- `GitCheckpointRequest`;
- `GitCheckpointDecision`;
- `GitCheckpointResult`;
- `GitCheckpointPolicy`;
- `GitCheckpointExecutionResult`.

O host recebe opcionalmente:

- `IGitCheckpointCoordinator`;
- `IGitCheckpointRequestSource`.

O request source pertence ao consumidor porque somente o consumidor conhece a
lista de arquivos esperados e sua política de autorização de branch. O Engine
controla quando a decisão é solicitada e quando o commit pode ser chamado.

## Política base

Um checkpoint é `Allowed` somente quando:

- task e remediation estão concluídas;
- validation e review passaram;
- a branch foi autorizada;
- existem alterações conhecidas;
- todos os arquivos alterados pertencem ao conjunto esperado;
- a mensagem segue formato Conventional Commit;
- `git diff --check` passou;
- o workspace está no estado esperado;
- não existem conflitos ou secrets detectados;
- a milestone, quando o escopo é milestone, está concluída e aprovada.

A política retorna `Blocked` para falhas operacionais e `HumanRequired` para
aprovação humana ausente ou estado explicitamente humano. Requests
inconsistentes retornam `Invalid`.

## Fluxo do EngineHost

```text
task executada
  → validation
  → review
  → remediation limitada
  → RunRecord Approved
  → request source do consumidor
  → GitCheckpointPolicy
  → IGitCheckpointCoordinator.EvaluateAsync
  → CommitAsync somente em Allowed
  → decisão e resultado persistidos no RunRecord/artifacts
```

Sem coordinator ou sem request source, a execução genérica continua normal e
nenhum commit automático é tentado.

`ApproveMilestoneAsync` somente solicita checkpoint depois de uma aprovação
explícita e nunca durante `CompleteAwaitingApproval`.

## Push e merge

O contrato não possui métodos de push ou merge. O `EngineHost` também rejeita
um resultado de coordinator que declare `PushPerformed` ou `MergePerformed`.
Essa é uma proteção adicional; uma implementação concreta ainda deve respeitar
o contrato e executar somente commit local.

## Persistência

Para tasks, são reutilizados `RunStore` e `RunRecord` existentes. São gravados:

- `git-checkpoint-decision.json`;
- `git-checkpoint-result.json`.

Não foi criada uma segunda persistência e nenhum artifact é escrito no
InfraSentinel neste milestone.

## GitWorkflowManager legado

O componente legado possui responsabilidades genéricas de inspeção e lifecycle,
mas também:

- assume branches históricas;
- prepara branches automaticamente;
- executa commit;
- publica a branch de feature;
- faz merge para a branch base;
- executa validação pós-merge;
- pode publicar a branch base.

Por isso ele não foi conectado diretamente ao novo contrato. Um futuro adapter
de compatibilidade deverá ficar fora do Core e bloquear push/merge automáticos.

## Compatibilidade e limites

- O contrato é opcional e não altera consumidores existentes.
- O Core não conhece InfraSentinel, OnlineOS, Flutter, Patrol, ADB ou AWS.
- Nenhum provider externo é iniciado pelo contrato.
- Ainda não existe uma implementação concreta de produção; este milestone
  fornece a fronteira e usa fakes somente nos testes.
- A integração futura do InfraSentinel deverá fornecer seu próprio coordinator
  e request source.
