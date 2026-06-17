using Lite.Serialization.Protobuf;

// A tiny TODO app. Tasks are persisted to a binary file using Lite.Serialization.Protobuf —
// no attributes on the models, no .proto written by hand.

const string dbPath = "todos.bin";

// ── Load: deserialize the whole store from disk (or start fresh) ──
TodoStore store = File.Exists(dbPath)
    ? LiteSerializer.DeserializeFrom<TodoStore>(File.ReadAllBytes(dbPath))
    : new TodoStore();

Console.WriteLine($"Loaded {store.Items.Count} task(s) from {dbPath}");

// ── Mutate ──
Add(store, "Buy milk");
Add(store, "Write the README");
Add(store, "Tag the 1.0 release");
Complete(store, id: 2);

// ── Save: serialize the whole store back to disk (size a buffer, write into it) ──
var bytes = new byte[LiteSerializer.ComputeSize(in store)];
LiteSerializer.SerializeTo(in store, bytes);
File.WriteAllBytes(dbPath, bytes);
Console.WriteLine($"Saved {store.Items.Count} task(s) → {dbPath} ({new FileInfo(dbPath).Length} bytes)");

// ── Show ──
Console.WriteLine();
Console.WriteLine("  TODO");
Console.WriteLine("  ────");
foreach (var t in store.Items)
    Console.WriteLine($"  [{(t.Done ? 'x' : ' ')}] #{t.Id,-3} {t.Title,-24} created {t.CreatedAt:HH:mm:ss}");

var doneCount = store.Items.Count(t => t.Done);
Console.WriteLine($"\n  {doneCount}/{store.Items.Count} done");

// ── The .proto schema is generated automatically — handy for other languages ──
Console.WriteLine("\n── generated schema (lite-proto export gives you this as a file) ──");
foreach (var (_, content) in ProtoSchemaRegistry.Enumerate(typeof(TodoStore).Assembly))
    Console.WriteLine(content);

// Tip: delete todos.bin to start over; re-run to see tasks accumulate.

static void Add(TodoStore store, string title)
{
    store.Items.Add(new TodoItem
    {
        Id = store.NextId++,
        Title = title,
        CreatedAt = DateTime.Now,
    });
    Console.WriteLine($"  + added: {title}");
}

static void Complete(TodoStore store, int id)
{
    var item = store.Items.FirstOrDefault(i => i.Id == id);
    if (item is { Done: false })
    {
        item.Done = true;
        Console.WriteLine($"  * completed #{id}: {item.Title}");
    }
}

// ── Models — plain POCO. No attributes, no base class. ──

public class TodoStore
{
    public int NextId { get; set; } = 1;
    public List<TodoItem> Items { get; set; } = [];
}

public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool Done { get; set; }
    public DateTime CreatedAt { get; set; }
}
