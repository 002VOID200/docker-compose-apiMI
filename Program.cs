using System.Text.Encodings.Web;
using System.Text.Unicode;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));

var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? "Host=localhost;Port=5432;Database=app;Username=postgres;Password=1234";
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

var app = builder.Build();

var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
await using (var create = dataSource.CreateCommand(
    "CREATE TABLE IF NOT EXISTS games (id SERIAL PRIMARY KEY, title TEXT NOT NULL, genre TEXT NOT NULL, available BOOLEAN NOT NULL DEFAULT TRUE)"))
{
    await create.ExecuteNonQueryAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/games", async (NpgsqlDataSource db) =>
{
    var list = new List<GameItem>();
    await using var cmd = db.CreateCommand("SELECT id, title, genre, available FROM games ORDER BY id");
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        list.Add(new GameItem(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
    }
    return Results.Ok(list);
});

app.MapGet("/games/{id:int}", async (int id, NpgsqlDataSource db) =>
{
    await using var cmd = db.CreateCommand("SELECT id, title, genre, available FROM games WHERE id = $1");
    cmd.Parameters.AddWithValue(id);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    return Results.Ok(new GameItem(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
});

app.MapPost("/games", async (GameInput input, NpgsqlDataSource db) =>
{
    await using var cmd = db.CreateCommand("INSERT INTO games (title, genre, available) VALUES ($1, $2, $3) RETURNING id");
    cmd.Parameters.AddWithValue(input.Title);
    cmd.Parameters.AddWithValue(input.Genre);
    cmd.Parameters.AddWithValue(input.Available);
    var id = (int)(await cmd.ExecuteScalarAsync());
    return Results.Created($"/games/{id}", new GameItem(id, input.Title, input.Genre, input.Available));
});

app.MapPut("/games/{id:int}", async (int id, GameInput input, NpgsqlDataSource db) =>
{
    await using var cmd = db.CreateCommand("UPDATE games SET title = $1, genre = $2, available = $3 WHERE id = $4");
    cmd.Parameters.AddWithValue(input.Title);
    cmd.Parameters.AddWithValue(input.Genre);
    cmd.Parameters.AddWithValue(input.Available);
    cmd.Parameters.AddWithValue(id);
    var rows = await cmd.ExecuteNonQueryAsync();
    return rows == 0 ? Results.NotFound() : Results.Ok(new GameItem(id, input.Title, input.Genre, input.Available));
});

app.MapDelete("/games/{id:int}", async (int id, NpgsqlDataSource db) =>
{
    await using var cmd = db.CreateCommand("DELETE FROM games WHERE id = $1");
    cmd.Parameters.AddWithValue(id);
    var rows = await cmd.ExecuteNonQueryAsync();
    return rows == 0 ? Results.NotFound() : Results.NoContent();
});

app.Run();

record GameItem(int Id, string Title, string Genre, bool Available);
record GameInput(string Title, string Genre, bool Available);
