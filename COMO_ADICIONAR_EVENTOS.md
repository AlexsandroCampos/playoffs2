# PRÓXIMOS PASSOS: INTEGRAR NOVO HANDLER DE FALTA

Se você quer adicionar suporte para eventos de falta (cartão amarelo/vermelho), é muito simples.

## 1️⃣ Registrar o Handler novo em Program.cs

```csharp
// worker/Program.cs

builder.Services.AddSingleton<IEventProjectionHandler, GoalScoredProjectionHandler>();
builder.Services.AddSingleton<IEventProjectionHandler, MatchEndedProjectionHandler>();

// ADICIONE ESTA LINHA:
builder.Services.AddSingleton<IEventProjectionHandler, FoulCommittedProjectionHandler>();
```

**Pronto!** O worker agora suporta eventos `FoulCommittedEvent`.

---

## 2️⃣ Estender IReadModelStore para cartões

Hoje a interface só tem:
```csharp
public interface IReadModelStore
{
    Task SaveStandingsAsync(int championshipId, string jsonPayload, CancellationToken ct);
    Task SaveStrikersAsync(int championshipId, string jsonPayload, CancellationToken ct);
}
```

Adicione para cartões:
```csharp
public interface IReadModelStore
{
    // ... métodos existentes ...
    
    // NOVO:
    Task SaveCardsAsync(int championshipId, string jsonPayload, CancellationToken ct);
}
```

---

## 3️⃣ Implementar SaveCardsAsync no Redis

Abra [worker/Infrastructure/RedisReadModelStore.cs](worker/Infrastructure/RedisReadModelStore.cs) e adicione:

```csharp
public async Task SaveCardsAsync(int championshipId, string jsonPayload, CancellationToken ct)
{
    var db = _redis.GetDatabase();
    var key = string.Format(_options.Redis.StandingsKeyPattern.Replace("standings", "cards"), championshipId);
    // Ou: var key = $"championship:{championshipId}:cards";
    await db.StringSetAsync(key, jsonPayload);
}
```

---

## 4️⃣ Adicionar query de cartões em IProjectionQueryRepository

```csharp
public interface IProjectionQueryRepository
{
    // ... existentes ...
    
    // NOVO:
    Task<string?> BuildCardsJsonAsync(int championshipId, CancellationToken ct);
}
```

---

## 5️⃣ Implementar BuildCardsJsonAsync no PostgreSQL

Abra [worker/Infrastructure/PostgresProjectionQueryRepository.cs](worker/Infrastructure/PostgresProjectionQueryRepository.cs) e adicione:

```csharp
public async Task<string?> BuildCardsJsonAsync(int championshipId, CancellationToken ct)
{
    const string query = @"
        SELECT json_agg(row_to_json(t))::text
        FROM (
            SELECT 
                COALESCE(f.PlayerTempId::text, f.PlayerId::text) AS playerId,
                u.name,
                COUNT(CASE WHEN f.YellowCard = true THEN 1 END) AS yellowCards,
                COUNT(CASE WHEN f.YellowCard = false THEN 1 END) AS redCards,
                tm.name AS teamName
            FROM Fouls f
            LEFT JOIN Users u ON f.PlayerId = u.Id
            LEFT JOIN PlayerTempProfiles p ON f.PlayerTempId = p.Id
            LEFT JOIN Matches m ON f.MatchId = m.Id
            LEFT JOIN Teams tm ON (u.PlayerTeamId = tm.Id OR p.TeamsId = tm.Id)
            WHERE m.ChampionshipId = @championshipId
            GROUP BY playerId, u.name, tm.name
            ORDER BY redCards DESC, yellowCards DESC
        ) t
    ";

    return await ExecuteProjectionJsonAsync(query, championshipId, ct);
}
```

---

## 🎯 RESUMO: ADICIONAR NOVO TIPO DE EVENTO

```
Novo evento (ex: Falta) 
    ↓
1. Criar handler (FoulCommittedProjectionHandler)
    ↓
2. Registrar no Program.cs
    ↓
3. Estender interfaces (IReadModelStore, IProjectionQueryRepository)
    ↓
4. Implementar as extensões (Redis + Postgres)
    ↓
✅ Pronto! Worker automático processa eventos de falta
```

---

## ❓ E SE FOR USAR RABBITMQ?

Nenhum desses passos muda!

Você só faz isso **UMA VEZ**:

```csharp
// Em Program.cs, substituir:
// De:
builder.Services.AddSingleton<IEventInboxReader, PostgresEventInboxReader>();

// Para:
builder.Services.AddSingleton<IEventInboxReader, RabbitMQEventInboxReader>();
```

E cria `RabbitMQEventInboxReader` que conecta ao RabbitMQ em vez de PostgreSQL.

**Todos os handlers continuam iguais!** 🎯

