using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");
var app = builder.Build();

app.MapGet("/health", async () =>
{
    try
    {
        await using var soedinenie = new NpgsqlConnection(StrokaPodklucheniya());
        await soedinenie.OpenAsync();
        await using var komanda = new NpgsqlCommand("select 1", soedinenie);
        await komanda.ExecuteScalarAsync();
        return Results.Ok(new { status = "ok", db = "ok" });
    }
    catch
    {
        return Results.Json(new { status = "error", db = "down" }, statusCode: 503);
    }
});

app.MapGet("/api/tickets", async (string? status, int? priority, string? q, string? sort, int? page, int? pageSize) =>
{
    if (status != null && !StatusOk(status))
        return Results.BadRequest(new { error = "status dolzhen byt new, in_progress ili done" });

    if (priority != null && (priority < 1 || priority > 3))
        return Results.BadRequest(new { error = "priority dolzhen byt ot 1 do 3" });

    var nomerStranicy = page ?? 1;
    var razmerStranicy = pageSize ?? 10;
    if (nomerStranicy < 1) nomerStranicy = 1;
    if (razmerStranicy < 1 || razmerStranicy > 100) razmerStranicy = 10;
    var napravlenie = sort == "asc" ? "asc" : "desc";

    var usloviya = new List<string>();

    await using var soedinenie = Podkluchitsya();
    await using var komanda = new NpgsqlCommand();
    komanda.Connection = soedinenie;

    if (status != null)
    {
        usloviya.Add("status = @status");
        komanda.Parameters.AddWithValue("status", status);
    }

    if (priority != null)
    {
        usloviya.Add("priority = @priority");
        komanda.Parameters.AddWithValue("priority", priority.Value);
    }

    if (!string.IsNullOrWhiteSpace(q))
    {
        usloviya.Add("title ilike @q");
        komanda.Parameters.AddWithValue("q", "%" + q + "%");
    }

    var gde = usloviya.Count > 0 ? " where " + string.Join(" and ", usloviya) : "";

    komanda.CommandText = "select count(*) from tickets" + gde;
    var vsego = Convert.ToInt64(await komanda.ExecuteScalarAsync());

    komanda.CommandText = "select id, title, description, priority, status, created_at from tickets" + gde
        + " order by created_at " + napravlenie + ", id " + napravlenie
        + " limit @limit offset @offset";
    komanda.Parameters.AddWithValue("limit", razmerStranicy);
    komanda.Parameters.AddWithValue("offset", (nomerStranicy - 1) * razmerStranicy);

    var spisok = new List<object>();
    await using (var chitatel = await komanda.ExecuteReaderAsync())
    {
        while (await chitatel.ReadAsync())
        {
            spisok.Add(new
            {
                id = chitatel.GetInt32(0),
                title = chitatel.GetString(1),
                description = chitatel.GetString(2),
                priority = chitatel.GetInt32(3),
                status = chitatel.GetString(4),
                createdAt = chitatel.GetDateTime(5)
            });
        }
    }

    return Results.Ok(new
    {
        total = vsego,
        page = nomerStranicy,
        pageSize = razmerStranicy,
        items = spisok
    });
});

app.MapGet("/api/tickets/{id:int}", async (int id) =>
{
    await using var soedinenie = Podkluchitsya();
    await using var komanda = new NpgsqlCommand(
        "select id, title, description, priority, status, created_at from tickets where id = @id", soedinenie);
    komanda.Parameters.AddWithValue("id", id);

    await using var chitatel = await komanda.ExecuteReaderAsync();
    if (!await chitatel.ReadAsync())
        return Results.NotFound(new { error = "zayavka ne naydena" });

    return Results.Ok(new
    {
        id = chitatel.GetInt32(0),
        title = chitatel.GetString(1),
        description = chitatel.GetString(2),
        priority = chitatel.GetInt32(3),
        status = chitatel.GetString(4),
        createdAt = chitatel.GetDateTime(5)
    });
});

app.MapPost("/api/tickets", async (Zayavka vhod) =>
{
    var oshibka = Proverit(vhod);
    if (oshibka != null)
        return Results.BadRequest(new { error = oshibka });

    var status = vhod.Status ?? "new";

    await using var soedinenie = Podkluchitsya();
    await using var komanda = new NpgsqlCommand(
        "insert into tickets (title, description, priority, status) values (@title, @description, @priority, @status) returning id",
        soedinenie);
    komanda.Parameters.AddWithValue("title", vhod.Title!.Trim());
    komanda.Parameters.AddWithValue("description", vhod.Description!.Trim());
    komanda.Parameters.AddWithValue("priority", vhod.Priority);
    komanda.Parameters.AddWithValue("status", status);

    var id = Convert.ToInt32(await komanda.ExecuteScalarAsync());

    return Results.Created("/api/tickets/" + id, new { id = id });
});

app.MapPut("/api/tickets/{id:int}", async (int id, Zayavka vhod) =>
{
    var oshibka = Proverit(vhod);
    if (oshibka != null)
        return Results.BadRequest(new { error = oshibka });

    var noviyStatus = vhod.Status ?? "new";

    await using var soedinenie = Podkluchitsya();

    string? stariyStatus;
    await using (var poisk = new NpgsqlCommand("select status from tickets where id = @id", soedinenie))
    {
        poisk.Parameters.AddWithValue("id", id);
        var rezultat = await poisk.ExecuteScalarAsync();
        if (rezultat == null)
            return Results.NotFound(new { error = "zayavka ne naydena" });
        stariyStatus = (string)rezultat;
    }

    await using (var obnovlenie = new NpgsqlCommand(
        "update tickets set title = @title, description = @description, priority = @priority, status = @status where id = @id",
        soedinenie))
    {
        obnovlenie.Parameters.AddWithValue("title", vhod.Title!.Trim());
        obnovlenie.Parameters.AddWithValue("description", vhod.Description!.Trim());
        obnovlenie.Parameters.AddWithValue("priority", vhod.Priority);
        obnovlenie.Parameters.AddWithValue("status", noviyStatus);
        obnovlenie.Parameters.AddWithValue("id", id);
        await obnovlenie.ExecuteNonQueryAsync();
    }

    if (stariyStatus != noviyStatus)
    {
        await using var sobytiye = new NpgsqlCommand(
            "insert into ticket_events (ticket_id, old_status, new_status) values (@ticket_id, @old_status, @new_status)",
            soedinenie);
        sobytiye.Parameters.AddWithValue("ticket_id", id);
        sobytiye.Parameters.AddWithValue("old_status", stariyStatus);
        sobytiye.Parameters.AddWithValue("new_status", noviyStatus);
        await sobytiye.ExecuteNonQueryAsync();
    }

    return Results.NoContent();
});

app.MapDelete("/api/tickets/{id:int}", async (int id) =>
{
    await using var soedinenie = Podkluchitsya();
    await using var komanda = new NpgsqlCommand("delete from tickets where id = @id", soedinenie);
    komanda.Parameters.AddWithValue("id", id);

    var skolko = await komanda.ExecuteNonQueryAsync();
    if (skolko == 0)
        return Results.NotFound(new { error = "zayavka ne naydena" });

    return Results.NoContent();
});

app.MapGet("/api/stats", async () =>
{
    await using var soedinenie = Podkluchitsya();
    await using var komanda = new NpgsqlCommand(
        "select count(*) filter (where status = 'new'), count(*) filter (where status = 'in_progress'), count(*) filter (where status = 'done'), count(*) from tickets",
        soedinenie);

    await using var chitatel = await komanda.ExecuteReaderAsync();
    await chitatel.ReadAsync();

    var poStatusam = new Dictionary<string, long>();
    poStatusam["new"] = chitatel.GetInt64(0);
    poStatusam["in_progress"] = chitatel.GetInt64(1);
    poStatusam["done"] = chitatel.GetInt64(2);

    return Results.Ok(new
    {
        total = chitatel.GetInt64(3),
        byStatus = poStatusam
    });
});

app.MapGet("/api/tickets/{id:int}/events", async (int id) =>
{
    await using var soedinenie = Podkluchitsya();
    await using var komanda = new NpgsqlCommand(
        "select id, ticket_id, old_status, new_status, changed_at from ticket_events where ticket_id = @id order by changed_at",
        soedinenie);
    komanda.Parameters.AddWithValue("id", id);

    var spisok = new List<object>();
    await using (var chitatel = await komanda.ExecuteReaderAsync())
    {
        while (await chitatel.ReadAsync())
        {
            spisok.Add(new
            {
                id = chitatel.GetInt32(0),
                ticketId = chitatel.GetInt32(1),
                oldStatus = chitatel.IsDBNull(2) ? null : chitatel.GetString(2),
                newStatus = chitatel.GetString(3),
                changedAt = chitatel.GetDateTime(4)
            });
        }
    }

    return Results.Ok(spisok);
});

app.Run();

string StrokaPodklucheniya()
{
    return Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Host=localhost;Port=5432;Database=clouddesk;Username=postgres;Password=postgres";
}

NpgsqlConnection Podkluchitsya()
{
    var soedinenie = new NpgsqlConnection(StrokaPodklucheniya());
    soedinenie.Open();
    return soedinenie;
}

static bool StatusOk(string status)
{
    return status == "new" || status == "in_progress" || status == "done";
}

static string? Proverit(Zayavka z)
{
    var title = z.Title?.Trim() ?? "";
    if (title.Length < 3 || title.Length > 120)
        return "title dolzhen byt ot 3 do 120 simvolov";

    if (string.IsNullOrWhiteSpace(z.Description))
        return "description obyazatelno";

    if (z.Priority < 1 || z.Priority > 3)
        return "priority dolzhen byt ot 1 do 3";

    if (z.Status != null && !StatusOk(z.Status))
        return "status dolzhen byt new, in_progress ili done";

    return null;
}

class Zayavka
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int Priority { get; set; }
    public string? Status { get; set; }
}
