using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;

if (args.Length == 0 || args is ["--help"])
{
    Console.WriteLine("ERP W3 — dry-run та контрольні opening у maintenance-вікні.");
    Console.WriteLine("dry-run --tenant <uuid> --location <uuid> [--max-rows 20000] [--output <новий.json>]");
    Console.WriteLine("materialize-control --tenant <uuid> --location <uuid> --run <uuid> --fingerprint <sha256> --batches <uuid,uuid> --reason <текст> --acceptance <посилання-на-staging-приймання> [--max-rows 20000] [--output <новий.json>]");
    Console.WriteLine("Підключення: SHELFGUARD_ERP_READ_CONNECTION для dry-run; окремий SHELFGUARD_ERP_WRITE_CONNECTION для materialize-control. Типового підключення немає.");
    Console.WriteLine("Для legacy store-scope RLS обом командам потрібен --operator <uuid> активного користувача tenant із призначенням на локацію.");
    return 0;
}
using var cancelled = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelled.Cancel(); };
string? temporary = null;
var writeRequested = args[0] == "materialize-control";
var commitConfirmed = false;
try
{
    if (args[0] is not ("dry-run" or "materialize-control") || args.Length % 2 != 1) throw new ArgumentException("Невідома команда або параметри.");
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i += 2)
        if (!(args[i] is "--tenant" or "--location" or "--max-rows" or "--output" or "--operator"
            || writeRequested && (args[i] is "--run" or "--fingerprint" or "--batches" or "--reason" or "--acceptance"))
            || !values.TryAdd(args[i], args[i + 1]))
            throw new ArgumentException("Невідомий або повторений параметр.");
    if (!Guid.TryParse(values.GetValueOrDefault("--tenant"), out var tenant) || tenant == Guid.Empty
        || !Guid.TryParse(values.GetValueOrDefault("--location"), out var location) || location == Guid.Empty)
        throw new ArgumentException("Потрібні явні непорожні tenant і location UUID.");
    if (!int.TryParse(values.GetValueOrDefault("--max-rows", "20000"), NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
        || limit is < 1 or > 100_000) throw new ArgumentException("max-rows має бути від 1 до 100000 для кожного потоку.");
    ControlBalanceRequest? request = null;
    Guid? operatorId = null;
    if (values.TryGetValue("--operator", out var operatorValue))
        operatorId = Guid.TryParse(operatorValue, out var parsed) && parsed != Guid.Empty ? parsed : throw new ArgumentException("Некоректний operator UUID.");
    if (writeRequested)
    {
        if (!Guid.TryParse(values.GetValueOrDefault("--run"), out var run)) throw new ArgumentException("Потрібен run UUID.");
        var batches = (values.GetValueOrDefault("--batches") ?? "").Split(',').Select(x =>
            Guid.TryParse(x, out var id) ? id : throw new ArgumentException("Некоректний batch UUID.")).ToArray();
        request = new(tenant, location, run, values.GetValueOrDefault("--fingerprint", ""), batches,
            values.GetValueOrDefault("--reason", ""), values.GetValueOrDefault("--acceptance", ""), limit, operatorId);
        ControlBalancePlanner.Validate(request);
    }
    var connectionVariable = writeRequested ? "SHELFGUARD_ERP_WRITE_CONNECTION" : "SHELFGUARD_ERP_READ_CONNECTION";
    var connection = Environment.GetEnvironmentVariable(connectionVariable);
    if (string.IsNullOrWhiteSpace(connection)) throw new ArgumentException("Вкажіть явне підключення; типового підключення немає.");
    var target = values.TryGetValue("--output", out var output) ? Path.GetFullPath(output) : null;
    if (target is not null && (File.Exists(target) || !Directory.Exists(Path.GetDirectoryName(target))))
        throw new ArgumentException("Файл звіту має бути новим, а його папка — існувати.");
    var builder = new NpgsqlConnectionStringBuilder(connection) { ApplicationName = writeRequested ? "ShelfGuard.ERP.ControlOpening" : "ShelfGuard.ERP.DryRun", MaxPoolSize = 2, Timeout = 10, CommandTimeout = 60, NoResetOnClose = false };
    await using var dataSource = new NpgsqlDataSourceBuilder(builder.ConnectionString).EnableDynamicJson().Build();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options);
    string json;
    if (request is not null)
    {
        var result = await new ControlBalanceMaterializer(db).MaterializeAsync(request, cancelled.Token);
        commitConfirmed = true;
        json = JsonSerializer.Serialize(result, InventoryDryRunJson.Options(indented: true));
    }
    else
    {
        var snapshot = await new InventorySnapshotReader(db).ReadAsync(tenant, location, limit, cancelled.Token, operatorId);
        json = JsonSerializer.Serialize(new InventoryDryRunAnalyzer().Analyze(snapshot), InventoryDryRunJson.Options(indented: true));
    }
    if (target is null) Console.WriteLine(json);
    else
    {
        temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, json + Environment.NewLine, new UTF8Encoding(false), cancelled.Token);
        File.Move(temporary, target, overwrite: false); temporary = null;
        Console.WriteLine($"{(writeRequested ? "Квитанцію запису" : "Dry-run")} збережено: {target}");
    }
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine(writeRequested ? "Операцію скасовано. Якщо commit уже почався, перевірте результат повтором ТОГО САМОГО run і параметрів." : "Dry-run скасовано; БД не змінювалася."); return 130; }
catch (MaterializationConflictException) { Console.Error.WriteLine("Конфлікт materialize-control: джерела/run змінилися, вибір непридатний, є резерви або активний writer. Нових записів цим запуском не зафіксовано; перевірте dry-run та параметри."); return 5; }
catch (SnapshotLimitExceededException) { Console.Error.WriteLine("Перевищено ліміт рядків. Частковий звіт не створено; звузьте перевірку або збільште max-rows."); return 3; }
catch (UnauthorizedAccessException) { Console.Error.WriteLine(commitConfirmed
    ? "Commit підтверджено, але немає доступу до файлу квитанції. Повторіть ТОЙ САМИЙ run і параметри з новим output."
    : "Локація недоступна в обраному tenant або немає доступу до файлу звіту."); return 4; }
catch (ArgumentException error) when (error.ParamName is null)
{
    // Only CLI validation messages are exposed; driver connection details and secrets are not printed.
    Console.Error.WriteLine("Некоректні параметри або дані. Скористайтеся --help."); return 2;
}
catch (Exception)
{
    Console.Error.WriteLine(writeRequested
        ? commitConfirmed ? "Commit підтверджено, але квитанцію не вдалося вивести. Повторіть ТОЙ САМИЙ run і параметри з новим output."
            : "Не вдалося завершити materialize-control. Результат commit може бути невідомим; перевірте повтором ТОГО САМОГО run і параметрів."
        : "Не вдалося завершити dry-run. Перевірте підключення, права читання та схему W2; БД не змінювалася.");
    return 1;
}
finally
{
    if (temporary is not null)
        try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
}
