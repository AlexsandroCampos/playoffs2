## ARQUITETURA DO WORKER - EXPLICAÇÃO COMPLETA

### 1️⃣ FLUXO DE DADOS (O QUE ACONTECE DURANTE A EXECUÇÃO)

```
┌─────────────────────────────────────────────────────────────────┐
│                    CICLO DO WORKER                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  1. WorkerService (Processing/WorkerService.cs)                 │
│     └─> Inicia loop a cada 3 segundos (configurável)            │
│         while (!stoppingToken.IsCancellationRequested) { ... }   │
│                                                                   │
│  2. Buscar Eventos Pendentes                                     │
│     └─> IEventInboxReader.FetchPendingAsync(50)                 │
│         Query PostgreSQL: SELECT * FROM outbox_events            │
│         WHERE status = 'Pending' LIMIT 50                        │
│         Retorna: List<InboxEvent>                                │
│                                                                   │
│  3. Para cada evento:                                            │
│     └─> IEventRouter.RouteAsync(evento)                         │
│         Procura handlers que suportam esse tipo de evento        │
│                                                                   │
│  4. Handler processa o evento                                    │
│     └─> GoalScoredProjectionHandler.HandleAsync()               │
│         (ou MatchEndedProjectionHandler, etc)                    │
│         ├─> IProjectionQueryRepository.BuildStandingsJsonAsync() │
│         │   └─> SELECT * FROM classifications ... (projeção)    │
│         └─> IReadModelStore.SaveStandingsAsync()                │
│             └─> Redis SET championship:5:standings <json>       │
│                                                                   │
│  5. Marcar evento como processado                                │
│     └─> IEventInboxReader.MarkProcessedAsync(eventId)           │
│         UPDATE outbox_events SET status = 'Processed'           │
│                                                                   │
│  6. Aguardar próximo ciclo (3 segundos)                         │
│     └─> Task.Delay(3000)                                        │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

### 2️⃣ CADA COMPONENTE EXPLICADO

#### 📋 Program.cs (Composição/Dependency Injection)
```csharp
// Registra as dependências no container do .NET
builder.Services.Configure<WorkerOptions>(...)          // Config de appsettings
builder.Services.AddSingleton<IEventInboxReader>        // Lê eventos
builder.Services.AddSingleton<IReadModelStore>          // Escreve Redis
builder.Services.AddSingleton<IProjectionQueryRepository> // Consulta Postgres
builder.Services.AddSingleton<IEventProjectionHandler>  // Handlers
builder.Services.AddHostedService<WorkerService>        // Loop principal
```
**Função**: Inicializar tudo quando a aplicação começa.

---

#### 🔄 WorkerService.cs (O coração do worker)
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)  // Loop infinito
    {
        // 1. Busca até 50 eventos pendentes
        var events = await _eventInboxReader.FetchPendingAsync(50);
        
        // 2. Processa cada evento
        foreach (var inboxEvent in events)
        {
            await _router.RouteAsync(inboxEvent);        // Roteia
            await _eventInboxReader.MarkProcessedAsync(); // Marca como OK
        }
        
        // 3. Aguarda 3 segundos antes de próxima busca
        await Task.Delay(TimeSpan.FromSeconds(3));
    }
}
```
**Função**: Loop que roda continuamente.
- A cada 3 segundos, tira eventos do PostgreSQL.
- Passa para processamento.
- Marca como processado.

---

#### 📡 PostgresEventInboxReader.cs (Lê eventos do Postgres)
```csharp
public async Task<IReadOnlyList<InboxEvent>> FetchPendingAsync(int batchSize)
{
    // Conecta ao PostgreSQL
    var query = "SELECT id, event_type, payload_json, occurred_at 
                 FROM outbox_events WHERE status = 'Pending' LIMIT @batchSize";
    
    // Executa query
    // Retorna lista de InboxEvent
}

public async Task MarkProcessedAsync(long eventId)
{
    // Marca evento como processado
    var cmd = "UPDATE outbox_events SET status = 'Processed' WHERE id = @id";
}
```
**Função**: Intermediário entre Postgres e o worker.

---

#### 🎯 EventRouter.cs (Roteia eventos para handlers)
```csharp
public async Task RouteAsync(InboxEvent inboxEvent)
{
    // Encontra handlers que suportam este tipo de evento
    var matchingHandlers = _handlers
        .Where(h => h.CanHandle(inboxEvent.EventType))  // GoalScoredEvent? MatchEnded?
        .ToList();
    
    // Executa todos os handlers que suportam
    foreach (var handler in matchingHandlers)
    {
        await handler.HandleAsync(inboxEvent);
    }
}
```
**Função**: Procura quem deve processar o evento (tipo "GoalScoredEvent" vai para GoalScoredProjectionHandler).

---

#### 🏆 GoalScoredProjectionHandler.cs (Handler específico)
```csharp
public bool CanHandle(string eventType)
{
    // Suporta esses tipos de evento
    return new[] { "GoalScoredEvent", "goal.scored" }.Contains(eventType);
}

public async Task HandleAsync(InboxEvent inboxEvent)
{
    // 1. Extrai championshipId do evento
    var championshipId = TryReadInt(inboxEvent.Payload, "championshipId");
    
    // 2. Consulta Postgres e constrói JSON das classificações
    var standingsJson = await _projectionQueries
        .BuildStandingsJsonAsync(championshipId);
    
    // 3. Salva snapshot no Redis
    await _readModelStore.SaveStandingsAsync(championshipId, standingsJson);
}
```
**Função**: Quando gol é marcado, recalcula e cacheia a tabela de classificação.

---

#### 💾 RedisReadModelStore.cs (Salva no Redis)
```csharp
public async Task SaveStandingsAsync(int championshipId, string jsonPayload)
{
    var db = _redis.GetDatabase();
    var key = $"championship:{championshipId}:standings";
    await db.StringSetAsync(key, jsonPayload);
    // Salva: championship:5:standings → "{json com ranking da competição}"
}
```
**Função**: Armazena snapshots pré-calculados.

---

#### 🔍 PostgresProjectionQueryRepository.cs (Consulta Postgres)
```csharp
public async Task<string?> BuildStandingsJsonAsync(int championshipId)
{
    // Query SQL que constrói JSON com rankings
    var query = @"
        SELECT json_agg(row_to_json(t))
        FROM (
            SELECT c.position, c.points, tm.name, tm.emblem
            FROM classifications c
            JOIN teams tm ON tm.id = c.teamid
            WHERE c.championshipid = @championshipId
            ORDER BY c.position
        ) t";
    
    return await ExecuteProjectionJsonAsync(query, championshipId);
}
```
**Função**: Executa queries pesadas de projeção e retorna JSON pronto.

---

### 3️⃣ RESPOSTA: RabbitMQ FUNCIONARIA?

**SIM! 100% compatível!**

Hoje a entrada de eventos é genérica via `IEventInboxReader`. Para trocar de PostgreSQL Outbox para RabbitMQ:

```csharp
// Atualmente: worker lê de PostgreSQL
builder.Services.AddSingleton<IEventInboxReader, PostgresEventInboxReader>();

// Com RabbitMQ seria:
builder.Services.AddSingleton<IEventInboxReader, RabbitMQEventInboxReader>();
```

Você só precisa criar uma classe `RabbitMQEventInboxReader` que implemente `IEventInboxReader`:

```csharp
public class RabbitMQEventInboxReader : IEventInboxReader
{
    public async Task<IReadOnlyList<InboxEvent>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        // Conecta ao RabbitMQ
        // Consome mensagens da fila
        // Retorna como InboxEvent
    }
    
    public async Task MarkProcessedAsync(long eventId, CancellationToken ct)
    {
        // Faz ACK na mensagem do RabbitMQ
    }
}
```

**Fluxo com RabbitMQ**:
```
RabbitMQ → RabbitMQEventInboxReader → EventRouter → Handlers → Redis
```

**Fluxo com PostgreSQL (hoje)**:
```
PostgreSQL Outbox → PostgresEventInboxReader → EventRouter → Handlers → Redis
```

O resto do código não muda! 🎯

---

### 4️⃣ RESPOSTA: SUPORTA DIFERENTES TIPOS DE EVENTOS?

**SIM! E é super fácil adicionar novos.**

Hoje temos 2 handlers:
- ✅ GoalScoredProjectionHandler
- ✅ MatchEndedProjectionHandler

Para adicionar **FoulEvent** (falta):

**Passo 1**: Criar novo handler
```csharp
// Processing/Handlers/FoulCommittedProjectionHandler.cs
public sealed class FoulCommittedProjectionHandler : IEventProjectionHandler
{
    private static readonly string[] SupportedEventTypes = new[] 
    { 
        "FoulCommittedEvent", 
        "foul.committed" 
    };

    public bool CanHandle(string eventType)
    {
        return SupportedEventTypes.Contains(eventType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(InboxEvent inboxEvent, CancellationToken ct)
    {
        var championshipId = TryReadInt(inboxEvent.Payload, "championshipId");
        
        // Recalcula cards
        var cardsJson = await _projectionQueries.BuildCardsJsonAsync(championshipId, ct);
        await _readModelStore.SaveCardsAsync(championshipId, cardsJson, ct);
    }
}
```

**Passo 2**: Registrar no Program.cs
```csharp
builder.Services.AddSingleton<IEventProjectionHandler, FoulCommittedProjectionHandler>();
```

**Pronto!** O EventRouter automaticamente encontrará e roteará eventos do tipo `FoulCommittedEvent`.

---

### 5️⃣ EXEMPLO COMPLETO: FLUXO DE UM GOL

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Árbitro registra gol na API                                  │
│    POST /matches/goals {matchId: 542, teamId: 3, playerId: ...} │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. API escreve na tabela GOALS e publica evento no OUTBOX       │
│    INSERT INTO goals (...)                                      │
│    INSERT INTO outbox_events (                                  │
│      event_type = 'GoalScoredEvent',                            │
│      payload_json = '{"matchId": 542, ...}',                    │
│      status = 'Pending'                                         │
│    )                                                             │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. Worker polling (a cada 3s):                                  │
│    SELECT FROM outbox_events WHERE status = 'Pending'           │
│    Encontra evento 'GoalScoredEvent' com id=1000                │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. EventRouter roteia para GoalScoredProjectionHandler           │
│    if (handler.CanHandle("GoalScoredEvent")) // true             │
│       await handler.HandleAsync(evento)                         │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 5. Handler executa:                                             │
│    a) Extrai championshipId = 5 do payload                      │
│    b) Consulta Postgres:                                        │
│       SELECT (classificações com pontos atualizados)            │
│    c) Constrói JSON:                                            │
│       [{pos: 1, team: "Bayern", points: 3}, ...]               │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 6. RedisReadModelStore salva:                                   │
│    SET championship:5:standings "{json com nova tabela}"        │
│    SET championship:5:strikers "{json com artilheiros}"         │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 7. Worker marca evento como processado:                         │
│    UPDATE outbox_events SET status = 'Processed' WHERE id=1000  │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 8. API consulta endpoints:                                      │
│    GET /statistics/5/classifications                            │
│    ├─> Tenta ler Redis primeiro (rápido!)                       │
│    └─> Se não existir, fallback para Postgres                   │
│    Retorna: Tabela atualizada em < 1ms                          │
└─────────────────────────────────────────────────────────────────┘
```

---

### 6️⃣ TIPOS DE EVENTOS QUE JÁ SUPORTARIA

| Evento | Handler | Trigger |
|--------|---------|---------|
| GoalScoredEvent | ✅ GoalScoredProjectionHandler | Quando gol é registrado |
| MatchEndedEvent | ✅ MatchEndedProjectionHandler | Quando partida termina |
| FoulCommittedEvent | ❌ (você cria) | Quando falta é registrada |
| PenaltyEvent | ❌ (você cria) | Quando pênalti é registrado |
| CardEvent | ❌ (você cria) | Quando cartão é dado |
| ReplacementEvent | ❌ (você cria) | Quando jogador é substituído |

---

### 7️⃣ PRÓXIMOS PASSOS

1. ✅ Seu colega implementa **RabbitMQ + Outbox da API** (já criada a interface genérica).
2. ✅ Você cria novos handlers conforme precisar de novos tipos de eventos.
3. ✅ API muda endpoints de leitura para primeiro consultar Redis (fallback PostgreSQL).
4. ✅ Worker roda como background service em produção.

