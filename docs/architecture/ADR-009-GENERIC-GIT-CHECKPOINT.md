# ADR-009 — Generic Git Checkpoint Contract

## Status

Accepted for M11-A; concrete consumer implementations deferred.

## Contexto

O Engine executava workflow e milestones, mas o commit estava acoplado ao
`GitWorkflowManager` histórico. Esse componente mistura lifecycle de branches,
commit, push e merge e contém pressupostos que não podem ser levados ao Core
neutro.

## Decisão

Adicionar ao `IAEngine.Core` um contrato opcional de checkpoint Git:

```text
IGitCheckpointCoordinator
GitCheckpointRequest
GitCheckpointDecision
GitCheckpointResult
GitCheckpointPolicy
```

O `EngineHost` depende apenas da abstração. O consumidor fornece a construção
do request e a implementação do coordinator. A política base do Core deve
bloquear estados incompletos, inconsistentes ou sem evidência suficiente.

O contrato executa somente commit local. Push, merge e publicação não são
operações do contrato.

## Consequências

### Positivas

- consumidores podem controlar seus próprios critérios de arquivos e branch;
- o workflow continua centralizado no EngineHost;
- validation, review e remediation são pré-condições verificáveis;
- ausência de coordinator é segura e compatível;
- decisões e resultados podem ser persistidos no RunStore existente;
- InfraSentinel poderá fornecer uma implementação sem alterar o Core.

### Negativas

- existe uma nova abstração pública no Core;
- o consumidor precisa fornecer um request source;
- ainda é necessário criar uma implementação concreta segura fora do Core;
- a API histórica continua coexistindo temporariamente.

## Alternativas rejeitadas

1. Reutilizar diretamente `GitWorkflowManager`: rejeitado por suas regras de
   branch, push e merge históricas.
2. Adicionar commit diretamente a `IGitService`: rejeitado porque misturaria
   leitura Git, política e mutação.
3. Criar um segundo executor de milestones: rejeitado por duplicar o workflow.
4. Implementar regras do InfraSentinel no Core: rejeitado por inversão de
   responsabilidade.

## Decisões adiadas

- adapter de compatibilidade para o CLI legado;
- implementação concreta do InfraSentinel;
- integração com publicação de Pull Request;
- aprovação humana persistida por identidade e evidência forte;
- remoção futura dos contratos históricos.
