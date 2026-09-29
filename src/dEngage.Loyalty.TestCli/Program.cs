using System.Text;
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using Spectre.Console;

const string PG = "Host=localhost;Database=loyalty_dev;Username=postgres;Password=postgres";
const string TENANT = "starbucks";

var factory = new ConnectionFactory { HostName = "localhost", Port = 5672, UserName = "guest", Password = "guest" };
using var conn = factory.CreateConnection();
using var channel = conn.CreateModel();

// Declare exchange + single queue (idempotent) — arguments must match the
// Consumer's exactly, otherwise RabbitMQ raises PRECONDITION_FAILED.
channel.ExchangeDeclare("loyalty.events", ExchangeType.Direct, durable: true);
channel.ExchangeDeclare("loyalty.events.dlx", ExchangeType.Fanout, durable: true);
channel.QueueDeclare("q.loyalty", durable: true, exclusive: false, autoDelete: false,
    arguments: new Dictionary<string, object> { ["x-dead-letter-exchange"] = "loyalty.events.dlx" });
channel.QueueDeclare("q.loyalty.dlq", durable: true, exclusive: false, autoDelete: false);
channel.QueueBind("q.loyalty.dlq", "loyalty.events.dlx", routingKey: "");
foreach (var rk in new[] { "order.created", "order.refunded", "cash.added", "cash.spent", "points.redeem", "points.transfer", "reward.purchase" })
    channel.QueueBind("q.loyalty", "loyalty.events", rk);

AnsiConsole.Write(new FigletText("Loyalty CLI").Color(Color.Green));
AnsiConsole.MarkupLine("[grey]tenant: starbucks | db: loyalty_dev | rabbit: localhost:5672[/]");
AnsiConsole.MarkupLine("[grey]Commands: order, refund, cash-add, cash-spend, redeem, balance, ledger, inbox, test, help, quit[/]\n");

// --test / --expire-test flag: run and exit
if (args.Contains("--test"))
{
    await IntegrationTest.RunAsync(channel);
    return;
}

if (args.Contains("--expire-test"))
{
    await ExpireTest.RunAsync();
    return;
}

if (args.Contains("--run-expire"))
{
    await ExpireTest.RunJobOnlyAsync();
    return;
}

if (args.Contains("--multi-tenant-test"))
{
    await MultiTenantTest.RunAsync(channel);
    return;
}

if (args.Contains("--reward-test"))
{
    await RewardTest.RunAsync(channel);
    return;
}

if (args.Contains("--outbound-test"))
{
    await OutboundTest.RunAsync(channel);
    return;
}

if (args.Contains("--transfer-test"))
{
    await TransferTest.RunAsync(channel);
    return;
}

if (args.Contains("--expire-verify"))
{
    await ExpireJobVerify.RunAsync();
    return;
}

if (args.Contains("--tier-verify"))
{
    await TierJobVerify.RunAsync();
    return;
}

if (args.Contains("--worker-verify"))
{
    await WorkerScheduleVerify.RunAsync();
    return;
}

if (args.Contains("--generic-test"))
{
    await GenericEventTest.RunAsync(channel);
    return;
}

if (args.Contains("--streak-test"))
{
    await StreakTest.RunAsync(channel);
    return;
}

if (args.Contains("--card-test"))
{
    await CardBucketTest.RunAsync(channel);
    return;
}

if (args.Contains("--tiqmo-test"))
{
    await TiqmoTest.RunAsync(channel);
    return;
}

var rnd = new Random();
string? lastOrderEventId = null;
string? lastContactKey = null;

while (true)
{
    var input = AnsiConsole.Prompt(new TextPrompt<string>("[green]>[/]").AllowEmpty());
    var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) continue;

    var cmd = parts[0].ToLower();

    try
    {
        switch (cmd)
        {
            case "quit":
            case "exit":
            case "q":
                return;

            case "help":
                PrintHelp();
                break;

            case "order":
                lastOrderEventId = await SendOrderAsync(parts);
                break;

            case "refund":
                await SendRefundAsync(parts, lastOrderEventId);
                break;

            case "cash-add":
                await SendCashAddAsync(parts);
                break;

            case "cash-spend":
                await SendCashSpendAsync(parts);
                break;

            case "redeem":
                await SendRedeemAsync(parts);
                break;

            case "balance":
                await ShowBalanceAsync(parts);
                break;

            case "ledger":
                await ShowLedgerAsync(parts);
                break;

            case "inbox":
                await ShowInboxAsync(parts);
                break;

            case "test":
                await IntegrationTest.RunAsync(channel);
                break;

            case "expire-test":
                await ExpireTest.RunAsync();
                break;

            case "reward-test":
                await RewardTest.RunAsync(channel);
                break;

            case "outbound-test":
                await OutboundTest.RunAsync(channel);
                break;

            default:
                AnsiConsole.MarkupLine($"[red]Unknown command: {cmd}[/] — type 'help'");
                break;
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
    }

    AnsiConsole.WriteLine();
}

// ─── Sending events ──────────────────────────────────────────────────────────

async Task<string> SendOrderAsync(string[] parts)
{
    // order [contact_key] [amount] [channel] [category]
    var contact  = parts.Length > 1 ? parts[1] : $"user_{rnd.Next(100, 999)}";
    var amount   = parts.Length > 2 ? decimal.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : rnd.Next(50, 500);
    var channel2 = parts.Length > 3 ? parts[3] : (rnd.Next(2) == 0 ? "mobile" : "store");
    var category = parts.Length > 4 ? parts[4] : (rnd.Next(2) == 0 ? "coffee" : "food");
    lastContactKey = contact;

    var eventId = Guid.NewGuid().ToString();
    var data = new
    {
        contact_key    = contact,
        amount         = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
        channel        = channel2,
        payment_method = "card",
        items          = new[] { new { sku = "SKU-001", category, qty = 1, total = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) } }
    };

    Publish("order.created", eventId, data);
    AnsiConsole.MarkupLine($"[yellow]→ order.created[/]  contact=[cyan]{contact}[/]  amount=[cyan]{amount:F2} TL[/]  channel=[cyan]{channel2}[/]  category=[cyan]{category}[/]");
    AnsiConsole.MarkupLine($"  event_id=[grey]{eventId}[/]");

    await WaitAndShowBalanceAsync(contact);
    return eventId;
}

async Task SendRefundAsync(string[] parts, string? originalEventId)
{
    // refund [contact_key] [original_event_id]
    var contact = parts.Length > 1 ? parts[1] : lastContactKey ?? "unknown";
    var origId  = parts.Length > 2 ? parts[2] : originalEventId;

    if (origId is null)
    {
        AnsiConsole.MarkupLine("[red]Send an order first or provide an event_id: refund <contact> <event_id>[/]");
        return;
    }

    var eventId = Guid.NewGuid().ToString();
    var data = new { contact_key = contact, original_event_id = origId, refund_ratio = "1.0" };

    Publish("order.refunded", eventId, data);
    AnsiConsole.MarkupLine($"[yellow]→ order.refunded[/]  contact=[cyan]{contact}[/]  original=[grey]{origId}[/]");
    await WaitAndShowBalanceAsync(contact);
}

async Task SendCashAddAsync(string[] parts)
{
    // cash-add [contact_key] [amount]
    var contact = parts.Length > 1 ? parts[1] : lastContactKey ?? $"user_{rnd.Next(100, 999)}";
    var amount  = parts.Length > 2 ? decimal.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 100m;
    lastContactKey = contact;

    var eventId = Guid.NewGuid().ToString();
    var data = new
    {
        contact_key     = contact,
        amount          = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
        account_type_id = "018fcd02-0000-7000-8000-000000000002" // Starbucks Card
    };

    Publish("cash.added", eventId, data);
    AnsiConsole.MarkupLine($"[yellow]→ cash.added[/]  contact=[cyan]{contact}[/]  amount=[cyan]{amount:F2} TL[/]");
    await WaitAndShowBalanceAsync(contact);
}

async Task SendCashSpendAsync(string[] parts)
{
    // cash-spend [contact_key] [amount]
    var contact = parts.Length > 1 ? parts[1] : lastContactKey ?? $"user_{rnd.Next(100, 999)}";
    var amount  = parts.Length > 2 ? decimal.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 50m;
    lastContactKey = contact;

    var eventId = Guid.NewGuid().ToString();
    var data = new
    {
        contact_key     = contact,
        amount          = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
        account_type_id = "018fcd02-0000-7000-8000-000000000002" // Starbucks Card
    };

    Publish("cash.spent", eventId, data);
    AnsiConsole.MarkupLine($"[yellow]→ cash.spent[/]  contact=[cyan]{contact}[/]  amount=[cyan]{amount:F2} TL[/]");
    await WaitAndShowBalanceAsync(contact);
}

async Task SendRedeemAsync(string[] parts)
{
    // redeem [contact_key] [points_amount]
    var contact = parts.Length > 1 ? parts[1] : lastContactKey ?? $"user_{rnd.Next(100, 999)}";
    var points  = parts.Length > 2 ? decimal.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 500m;
    lastContactKey = contact;

    var eventId = Guid.NewGuid().ToString();
    var data = new
    {
        contact_key             = contact,
        points_amount           = points.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
        source_account_type_id  = "018fcd02-0000-7000-8000-000000000001" // Stars
    };

    Publish("points.redeem", eventId, data);
    AnsiConsole.MarkupLine($"[yellow]→ points.redeem[/]  contact=[cyan]{contact}[/]  points=[cyan]{points:F0} Star[/]");
    await WaitAndShowBalanceAsync(contact);
}

// ─── DB queries ──────────────────────────────────────────────────────────────

async Task ShowBalanceAsync(string[] parts)
{
    var contact = parts.Length > 1 ? parts[1] : lastContactKey ?? "";
    if (string.IsNullOrEmpty(contact)) { AnsiConsole.MarkupLine("[red]contact_key required: balance <contact>[/]"); return; }
    await PrintBalancesAsync(contact);
}

async Task WaitAndShowBalanceAsync(string contact)
{
    await Task.Delay(900); // Give the Consumer time to process
    await PrintBalancesAsync(contact);
}

async Task PrintBalancesAsync(string contact)
{
    await using var db = new NpgsqlConnection(PG);
    await db.OpenAsync();

    const string sql = """
        SELECT at.name, at.type, ca.balance
        FROM customer_accounts ca
        JOIN account_types at ON at.id = ca.account_type_id
        WHERE ca.tenant_id = @t AND ca.contact_key = @c
        ORDER BY at.type
        """;

    await using var cmd = new NpgsqlCommand(sql, db);
    cmd.Parameters.AddWithValue("t", TENANT);
    cmd.Parameters.AddWithValue("c", contact);
    await using var reader = await cmd.ExecuteReaderAsync();

    var table = new Table().Border(TableBorder.Rounded).Title($"[bold]Balance — {contact}[/]");
    table.AddColumn("Account");
    table.AddColumn("Type");
    table.AddColumn(new TableColumn("Balance").RightAligned());

    bool hasRows = false;
    while (await reader.ReadAsync())
    {
        hasRows = true;
        var name  = reader.GetString(0);
        var type  = reader.GetString(1);
        var bal   = reader.GetDecimal(2);
        var color = type switch { "POINTS" => "yellow", "CASH" => "green", "STAMP" => "blue", _ => "white" };
        var unit  = type switch { "POINTS" => "★", "CASH" => "TL", "STAMP" => "stamps", _ => "" };
        table.AddRow(name, $"[{color}]{type}[/]", $"[bold {color}]{bal:F2} {unit}[/]");
    }

    if (!hasRows)
        table.AddRow("[grey]—[/]", "[grey]no account yet — has the Consumer processed it?[/]", "[grey]0[/]");

    AnsiConsole.Write(table);
}

async Task ShowLedgerAsync(string[] parts)
{
    // ledger [contact_key] [limit]
    var contact = parts.Length > 1 ? parts[1] : lastContactKey ?? "";
    var limit   = parts.Length > 2 ? int.Parse(parts[2]) : 10;
    if (string.IsNullOrEmpty(contact)) { AnsiConsole.MarkupLine("[red]contact_key required: ledger <contact>[/]"); return; }

    await using var db = new NpgsqlConnection(PG);
    await db.OpenAsync();

    const string sql = """
        SELECT le.created_at, le.reason, le.delta, at.name, le.source_event_id
        FROM ledger_entries le
        JOIN customer_accounts ca ON ca.id = le.customer_account_id
        JOIN account_types at ON at.id = ca.account_type_id
        WHERE le.tenant_id = @t AND le.contact_key = @c
        ORDER BY le.created_at DESC
        LIMIT @lim
        """;

    await using var cmd = new NpgsqlCommand(sql, db);
    cmd.Parameters.AddWithValue("t", TENANT);
    cmd.Parameters.AddWithValue("c", contact);
    cmd.Parameters.AddWithValue("lim", limit);
    await using var reader = await cmd.ExecuteReaderAsync();

    var table = new Table().Border(TableBorder.Simple).Title($"[bold]Ledger — {contact} (last {limit})[/]");
    table.AddColumn("Time");
    table.AddColumn("Reason");
    table.AddColumn(new TableColumn("Delta").RightAligned());
    table.AddColumn("Account");
    table.AddColumn("Event");

    bool hasRows = false;
    while (await reader.ReadAsync())
    {
        hasRows = true;
        var ts     = reader.GetDateTime(0).ToLocalTime().ToString("HH:mm:ss");
        var reason = reader.GetString(1);
        var delta  = reader.GetDecimal(2);
        var acct   = reader.GetString(3);
        var evId   = reader.GetString(4);
        var short_ = evId.Length > 8 ? evId[..8] + "…" : evId;
        var color  = delta >= 0 ? "green" : "red";
        var sign   = delta >= 0 ? "+" : "";
        table.AddRow(ts, reason, $"[{color}]{sign}{delta:F2}[/]", acct, $"[grey]{short_}[/]");
    }

    if (!hasRows) table.AddRow("[grey]—[/]", "[grey]no records[/]", "", "", "");
    AnsiConsole.Write(table);
}

async Task ShowInboxAsync(string[] parts)
{
    var limit = parts.Length > 1 ? int.Parse(parts[1]) : 5;

    await using var db = new NpgsqlConnection(PG);
    await db.OpenAsync();

    const string sql = """
        SELECT event_id, event_type, status, received_at, error
        FROM event_inbox
        WHERE tenant_id = @t
        ORDER BY received_at DESC
        LIMIT @lim
        """;

    await using var cmd = new NpgsqlCommand(sql, db);
    cmd.Parameters.AddWithValue("t", TENANT);
    cmd.Parameters.AddWithValue("lim", limit);
    await using var reader = await cmd.ExecuteReaderAsync();

    var table = new Table().Border(TableBorder.Simple).Title($"[bold]Event Inbox (last {limit})[/]");
    table.AddColumn("Event ID");
    table.AddColumn("Type");
    table.AddColumn("Status");
    table.AddColumn("Time");
    table.AddColumn("Error");

    while (await reader.ReadAsync())
    {
        var id     = reader.GetString(0);
        var short_ = id.Length > 8 ? id[..8] + "…" : id;
        var type   = reader.GetString(1);
        var status = reader.GetString(2);
        var ts     = reader.GetDateTime(3).ToLocalTime().ToString("HH:mm:ss");
        var error  = reader.IsDBNull(4) ? "" : reader.GetString(4);
        var color  = status == "processed" ? "green" : status == "failed" ? "red" : "yellow";
        table.AddRow($"[grey]{short_}[/]", type, $"[{color}]{status}[/]", ts, $"[red]{error}[/]");
    }

    AnsiConsole.Write(table);
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

void Publish(string routingKey, string eventId, object data)
{
    var envelope = new
    {
        EventId    = eventId,
        EventType  = routingKey,
        Tenant     = TENANT,
        OccurredAt = DateTime.UtcNow,
        Version    = "1",
        Data       = data
    };

    var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    var body = Encoding.UTF8.GetBytes(json);

    var props = channel.CreateBasicProperties();
    props.Persistent  = true;
    props.ContentType = "application/json";

    channel.BasicPublish("loyalty.events", routingKey, props, body);
}

void PrintHelp()
{
    var table = new Table().Border(TableBorder.Rounded).Title("[bold]Commands[/]");
    table.AddColumn("Command");
    table.AddColumn("Parameters");
    table.AddColumn("Description");
    table.AddColumn("Example");

    table.AddRow("[yellow]order[/]",     "[contact] [amount] [channel] [category]", "Send an order event",  "order ahmet 250 mobile coffee");
    table.AddRow("[yellow]refund[/]",    "[contact] [event_id]",                    "Refund the last order", "refund ahmet");
    table.AddRow("[yellow]cash-add[/]",  "[contact] [amount]",                      "Load money onto the card", "cash-add ahmet 100");
    table.AddRow("[yellow]cash-spend[/]","[contact] [amount]",                      "Spend from the card",  "cash-spend ahmet 50");
    table.AddRow("[yellow]redeem[/]",    "[contact] [points]",                      "Convert Stars → TL",   "redeem ahmet 500");
    table.AddRow("[cyan]balance[/]",     "[contact]",                               "Show balance",          "balance ahmet");
    table.AddRow("[cyan]ledger[/]",      "[contact] [limit]",                       "Ledger movements",      "ledger ahmet 20");
    table.AddRow("[cyan]inbox[/]",       "[limit]",                                 "Event inbox status",    "inbox 10");
    table.AddRow("[magenta]test[/]",      "",                                        "Run the 12 integration tests", "test");
    table.AddRow("[magenta]expire-test[/]", "",                                     "Points expire FIFO scenario",  "expire-test");
    table.AddRow("[grey]quit / q[/]",    "",                                        "Exit",                  "q");

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine("\n[grey]All parameters are optional — leaving one blank generates a random value.[/]");
    AnsiConsole.MarkupLine("[grey]Note: the Consumer must be running. Balance updates with a ~1s delay.[/]");
}
