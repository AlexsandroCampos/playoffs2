# PlayOffs Worker

Worker dedicado para projecoes de leitura (Redis) a partir de eventos.

## Objetivo

- Consumir eventos de uma origem configuravel (por enquanto via tabela/event inbox no PostgreSQL).
- Recarregar read models no Redis.
- Deixar a API focada em escrita e validacoes transacionais.

## Stack recomendada

- .NET Worker Service (mesma stack da API atual)
- PostgreSQL via Npgsql
- Redis via StackExchange.Redis

## Estrutura criada

- Program.cs: composicao DI e bootstrap.
- Processing/WorkerService.cs: loop de polling.
- Infrastructure/PostgresEventInboxReader.cs: leitura e ACK de eventos (generico).
- Processing/EventRouter.cs: roteia por tipo de evento.
- Processing/Handlers: handlers de projeção.
- Infrastructure/PostgresProjectionQueryRepository.cs: consulta projecoes no PostgreSQL.
- Infrastructure/RedisReadModelStore.cs: grava snapshots no Redis.

## Como rodar

1. Ajuste as conexoes em appsettings.json.
2. Ajuste os SQLs genericos para o contrato final da tabela/eventos.
3. Rode:

   dotnet run --project worker/PlayOffs.Worker.csproj

## Dependencias do companheiro (deixar generico por agora)

Esses pontos dependem da implementacao final do broker/tabela de eventos:

1. Nome real da tabela e colunas de eventos.
   - Hoje o worker assume colunas: id, event_type, payload_json, occurred_at, status.
2. Estrategia de ACK/processamento.
   - Hoje o worker marca Processed com um UPDATE simples.
3. Envelope do evento.
   - Hoje handlers aceitam eventType GoalScoredEvent/goal.scored e MatchEndedEvent/match.ended.
   - Payload esperado com championshipId ou matchId.

Quando essa parte ficar pronta, ajuste apenas:

- Worker:Postgres:FetchPendingEventsQuery
- Worker:Postgres:MarkEventProcessedCommand
- Worker:Postgres:ResolveChampionshipByMatchIdQuery
- Event types nos handlers

## Proximo passo sugerido

Implementar os endpoints de leitura da API consultando Redis primeiro e fallback para PostgreSQL durante transicao.
