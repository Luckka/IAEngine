# M10 — Legacy Boundary Stabilization

## Objetivo

O M10 estabiliza a fronteira entre `IAEngine.Core`,
`IAEngine.OnlineOSAdapter`, o CLI legado e consumidores externos. O milestone
não renomeia a API pública histórica nem remove o adapter.

## Estado antes

A auditoria de independência confirmou que o Core e o InfraSentinel funcionam
sem o checkout do OnlineOS, mas identificou três problemas de fronteira:

1. a configuração distribuída como `appsettings.json` era implicitamente
   OnlineOS;
2. a suíte de testes era uma combinação de Core, adapter e compatibilidade;
3. uma solução consumidora limpa podia falhar ao localizar o reference
   assembly do Core (`CS0006`).

Uma reprodução correta da suíte, usando `dotnet test` com build automático,
passou os 189 testes legados. A reprodução anterior com `--no-build` em uma
cópia sem `bin/obj` não era uma falha funcional da suíte.

## Alterações implementadas

- `appsettings.json` agora descreve um perfil `iaengine-generic` sem providers,
  validators ou capabilities OnlineOS.
- A configuração histórica foi preservada em `appsettings.onlineos.json` e só
  é selecionada explicitamente, por exemplo através de
  `ONLINEOS_ORCHESTRATOR_CONFIG`.
- `appsettings.example.json` também é genérico; o exemplo legado foi preservado
  em `appsettings.onlineos.example.json`.
- O CLI rejeita comandos que exigem composição quando o perfil genérico não
  possui host registration, antes de instanciar providers.
- A mensagem e o título do CLI deixaram de assumir OnlineOS no caminho genérico.
- `IAEngine.Core` passou a produzir reference assembly para consumidores
  ProjectReference em soluções limpas.
- Cinco testes puramente genéricos foram incluídos no projeto
  `IAEngine.Core.Tests`.
- Onze testes de agents, Flutter, Patrol, ADB, prototype e reference inspection
  foram isolados em `IAEngine.OnlineOSAdapter.Tests`.
- Nenhum teste foi apagado; a suíte de compatibilidade restante continua no
  projeto `OnlineOs.AiOrchestrator.Tests`.

## Organização atual dos testes

| Projeto | Responsabilidade | Resultado |
|---|---|---:|
| `IAEngine.Core.Tests` | Core, host, composição de boundary e infraestrutura genérica | 22 testes |
| `IAEngine.OnlineOSAdapter.Tests` | Agents, Flutter, Patrol, ADB, prototype e reference | 73 testes |
| `OnlineOs.AiOrchestrator.Tests` | Compatibilidade legada e testes históricos restantes | 97 testes |

O total preservado é 192 testes contando o teste Core original, os novos testes
de configuração e os testes legados reorganizados. A separação é por projeto;
os arquivos históricos continuam no diretório `tests/` para evitar mudanças
desnecessárias de caminho nesta etapa.

## Direção das dependências

```text
IAEngine.OnlineOSAdapter → IAEngine.Core
OnlineOs.AiOrchestrator → IAEngine.OnlineOSAdapter
InfraSentinel → IAEngine.Core
```

Não foi adicionada nenhuma referência do Core ao adapter, nem do InfraSentinel
ao adapter.

## Dependências históricas mantidas

Ainda permanecem, deliberadamente:

- namespace público `OnlineOs.AiOrchestrator`;
- opções Flutter/Patrol/ADB no Core;
- categorias de falha históricas;
- campos de prototype;
- branches `developer` e `producao`;
- CLI legado e composição OnlineOS;
- testes históricos de compatibilidade.

Esses elementos não foram removidos porque ainda fazem parte da superfície de
compatibilidade. A remoção exige uma decisão de API major.

## Limitações e riscos

- O CLI legado continua dependente do adapter quando a configuração OnlineOS é
  selecionada explicitamente.
- O Core ainda não é semanticamente neutro em todos os modelos públicos.
- A suíte de compatibilidade ainda possui testes históricos mistos que deverão
  ser reduzidos em milestone posterior.
- Renomear namespaces ou remover campos pode quebrar consumidores externos,
  persistência e o CLI legado.

## Verificação

Os comandos foram executados sequencialmente:

```text
dotnet build src/IAEngine.Core/IAEngine.Core.csproj
dotnet test tests/IAEngine.Core.Tests/IAEngine.Core.Tests.csproj
dotnet build src/IAEngine.OnlineOSAdapter/IAEngine.OnlineOSAdapter.csproj
dotnet test tests/IAEngine.OnlineOSAdapter.Tests/IAEngine.OnlineOSAdapter.Tests.csproj
dotnet build OnlineOs.AiOrchestrator.csproj
dotnet test tests/OnlineOs.AiOrchestrator.Tests.csproj
dotnet build InfraSentinel.sln
dotnet test InfraSentinel.sln
```

Nenhum provider real, AWS, Flutter, Patrol, ADB ou infraestrutura externa foi
executado.

## Próximos passos

1. decidir a política de remoção dos namespaces históricos;
2. separar os testes de compatibilidade que ainda dependem de modelos legados;
3. definir uma API major para remover opções Flutter/Patrol/ADB do Core;
4. somente depois remover o adapter OnlineOS, se ainda houver autorização.
