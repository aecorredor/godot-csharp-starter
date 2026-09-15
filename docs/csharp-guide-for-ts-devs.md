# C# for Game Developers: A Guide for TypeScript/Systems Programmers

**Scope:** **.NET 8+** (C# **12**), engine-neutral. Examples use standard library types (`System.Numerics`, `Span<T>`, events) — not Unity, Godot, or Unreal APIs.

**Audience:** Developers who know TypeScript or systems languages and want idiomatic C# for real-time games — not a full C# tutorial.

---

## The Mental Model Shift

**TypeScript**: Everything is an object on the heap, GC handles it all, you rarely think about memory.

**C/Zig**: You control everything—stack vs heap, allocation, deallocation.

**C#**: A middle ground. You get GC convenience but can opt into low-level control when performance matters. The key is knowing *when* to care.

---

## 1. Variables and Type Inference

```csharp
using System.Numerics;

// Type inference with var (like TS's let/const with inference)
var health = 100;              // int
var speed = 5.5f;              // float (f suffix required!)
var name = "Player";           // string
var position = Vector3.Zero;   // System.Numerics.Vector3

// Explicit types when clarity matters
float delta = 0.016f;
Dictionary<long, PlayerData> players = new();

// Constants (compile-time, inlined)
const float MAX_SPEED = 10.0f;
const string PLAYER_GROUP = "players";

// Static readonly (runtime constant, can be computed)
static readonly Vector3 SpawnPoint = new(0, 5, 0);
```

**Key difference from TS**: Numbers have distinct types. `5.5` is `double`, `5.5f` is `float`. Game math usually uses `float` — always use the `f` suffix when you mean float.

---

## 2. Nullability

```csharp
// Enable project-wide in .csproj (preferred for new projects):
// <Nullable>enable</Nullable>

// Non-nullable (compiler warns if you might assign null)
string name = "Player";
PlayerController controller;  // Must be assigned before use

// Nullable (explicit "this can be null")
string? displayName = null;
PlayerController? target = null;

// Null checks (similar to TS)
if (target is not null)
{
    target.TakeDamage(10);
}

// Null conditional (like TS's ?.) — only when reference is legitimately optional
target?.TakeDamage(10);

// Null coalescing (like TS's ??)
var actualName = displayName ?? "Unknown";

// Null forgiving (like TS's !) — when you know init runs before use
PlayerController controller = GetRequiredService<PlayerController>()!;
WeaponView weaponView = null!;  // assigned in Initialize(), before Update runs
```

**Game pattern:** references wired during `Initialize()` / `Awake()` / scene load use `= null!` when the compiler cannot see engine-driven initialization. For optional lookups, use `T?` and handle null explicitly — do not silently `?.` on required dependencies.

**Why nullable reference types?** Catches null bugs at compile time. Always enable for new C# projects.

---

## 3. Value Types vs Reference Types

This is the **most important concept** for performance.

```csharp
using System.Numerics;

// VALUE TYPES (stack allocated, copied on assignment)
// - All numeric types: int, float, double, long, bool
// - struct (including Vector3, Matrix4x4, Color-like structs)
// - enum

Vector3 a = new(1, 2, 3);
Vector3 b = a;        // b is a COPY
b.X = 999f;
// a.X is still 1!

// REFERENCE TYPES (heap allocated, shared reference)
// - class
// - arrays
// - string (immutable reference type)
// - delegates

PlayerController a = new();
PlayerController b = a;  // b points to SAME object
b.Health = 0;
// a.Health is also 0!
```

**TypeScript comparison**: Everything in TS is like a reference type. Primitives are immutable so you don't notice.

**Why this matters in games**: math and transform types are usually structs. Modifying a copy does not affect the original — intentional, but easy to get wrong when passing by value.

---

## 4. Structs vs Classes

```csharp
using System.Numerics;

// STRUCT: Use for small, immutable data
public readonly struct InputSnapshot
{
    public readonly float MoveX;
    public readonly float MoveY;
    public readonly bool Jump;
    public long Timestamp { get; }

    public InputSnapshot(float moveX, float moveY, bool jump, long timestamp)
    {
        MoveX = moveX;
        MoveY = moveY;
        Jump = jump;
        Timestamp = timestamp;
    }
}

// CLASS: Use for larger objects with behavior and identity
public class PlayerController
{
    private int _health = 100;

    public void TakeDamage(int amount) => _health -= amount;
}
```

**When to use struct**:
- Small (≤16 bytes ideally)
- Immutable
- Represents a "value" (position, color, input state)
- Frequently created/destroyed (avoids GC pressure)

**DTO (Data Transfer Object):** a struct or class whose job is to **carry data across a boundary** (network wire, save file, API) with little behavior. Network snapshots (`InputSnapshot`, player state bytes) are usually `readonly struct` + explicit `ToBytes`/`FromBytes` — see §24.

**When to use class**:
- Has mutable state
- Has complex behavior
- Needs inheritance
- Represents an "entity" with identity

---

## 5. The `readonly` Keyword

```csharp
// readonly FIELD: Can only be assigned in constructor
public class Player
{
    private readonly int _maxHealth;

    public Player(int maxHealth)
    {
        _maxHealth = maxHealth;  // OK
    }

    public void Something()
    {
        _maxHealth = 200;  // ERROR: Can't assign to readonly
    }
}

// readonly STRUCT: All fields are implicitly readonly
public readonly struct Vector2
{
    public readonly float X;
    public readonly float Y;
    // Can't modify after construction
}

// readonly METHOD PARAMETER (C# 12): Prevents accidental modification
public void Process(readonly ref Vector3 position)
{
    position.X = 5;  // ERROR: position is readonly
}
```

**Why care?** `readonly struct` enables compiler optimizations and prevents bugs. Always use for network state, math types, etc.

---

## 6. Properties vs Fields

```csharp
public class Player
{
    // FIELD: Direct data storage
    private int _health;

    // PROPERTY: Accessor with optional logic
    public int Health
    {
        get => _health;
        set => _health = Math.Clamp(value, 0, MaxHealth);
    }

    // AUTO-PROPERTY: Compiler generates backing field
    public int MaxHealth { get; set; } = 100;

    // READONLY PROPERTY: Only getter
    public bool IsDead => Health <= 0;

    // INIT-ONLY PROPERTY (C# 9+): Set once during construction
    public string Name { get; init; }
}

// Usage
var player = new Player { Name = "Bob", MaxHealth = 150 };
player.Name = "Alice";  // ERROR: init-only
```

**Convention**: Public API uses properties, private implementation uses fields with `_` prefix.

---

## 7. ref, in, out Parameters

```csharp
// BY VALUE (default): Copy passed in
void Modify(Vector3 v)
{
    v.X = 999;  // Modifies local copy only
}

// REF: Pass by reference, can modify original
void Modify(ref Vector3 v)
{
    v.X = 999;  // Modifies original!
}

// IN: Pass by reference, but readonly (no copy, no modification)
void Read(in Vector3 v)
{
    var x = v.X;  // OK
    v.X = 999;    // ERROR: readonly
}

// OUT: Must assign before returning
bool TryGetPlayer(long id, out Player player)
{
    if (_players.TryGetValue(id, out player))
        return true;

    player = null!;
    return false;
}

// Usage
if (TryGetPlayer(123, out var player))
{
    player.TakeDamage(10);
}
```

**Performance tip**: Use `in` for large structs (>16 bytes) to avoid copying. Common in game dev.

---

## 8. Pattern Matching

```csharp
// Type patterns (better than is + cast)
if (body is PlayerController player)
{
    player.TakeDamage(10);
}

// Switch expressions (like TS switch but more powerful)
string GetPoseAnimation(CharacterPose pose) => pose switch
{
    CharacterPose.Standing => "Idle",
    CharacterPose.Crouching => "Crouch",
    CharacterPose.Proning => "Prone",
    _ => throw new ArgumentOutOfRangeException(nameof(pose))
};

// Property patterns
string Describe(Player p) => p switch
{
    { Health: 0 } => "Dead",
    { Health: < 20, IsSprinting: true } => "Dying but running",
    { Health: < 20 } => "Critical",
    _ => "Healthy"
};

// List patterns (C# 11+)
int[] numbers = [1, 2, 3];
var result = numbers switch
{
    [1, 2, 3] => "Exact match",
    [1, ..] => "Starts with 1",
    [.., 3] => "Ends with 3",
    _ => "Something else"
};
```

**Prefer `is T` over `as T`** when the type is expected: `as` fails silently (null); `is` lets you log the actual type on mismatch.

---

## 9. Collections

```csharp
// ARRAY: Fixed size, fastest access
int[] scores = new int[10];
int[] preInit = [1, 2, 3, 4, 5];  // Collection expression (C# 12)

// LIST: Dynamic size, still fast
List<Player> players = new();
players.Add(player);
var first = players[0];

// DICTIONARY: Key-value lookup
Dictionary<long, Player> playersById = new();
playersById[peerId] = player;
if (playersById.TryGetValue(peerId, out var p)) { }

// HASHSET: Unique values, fast contains
HashSet<long> connectedPeers = new();
connectedPeers.Add(peerId);
if (connectedPeers.Contains(peerId)) { }

// QUEUE: FIFO
Queue<InputSnapshot> pendingInputs = new();
pendingInputs.Enqueue(input);
var next = pendingInputs.Dequeue();

// Collection expressions (C# 12) - works for all collection types
List<int> list = [1, 2, 3];
int[] array = [1, 2, 3];
HashSet<int> set = [1, 2, 3];
```

---

## 10. LINQ (Language Integrated Query)

```csharp
List<Player> players = GetAllPlayers();

// Query syntax (SQL-like)
var alivePlayers = from p in players
                   where p.Health > 0
                   orderby p.Score descending
                   select p;

// Method syntax (more common, like JS array methods)
var alivePlayers = players
    .Where(p => p.Health > 0)
    .OrderByDescending(p => p.Score)
    .ToList();

// Common operations
players.Any(p => p.Health <= 0);      // true if any dead
players.All(p => p.Health > 0);       // true if all alive
players.First(p => p.Name == "Bob");  // throws if not found
players.FirstOrDefault(p => p.Name == "Bob");  // null if not found
players.Count(p => p.Health > 50);    // count matching
players.Sum(p => p.Score);            // aggregate
players.Select(p => p.Name);          // map to new type
players.ToDictionary(p => p.Id);      // convert to dict
```

**Warning for games**: LINQ allocates. Avoid in hot paths (per-frame update, fixed tick, network handlers). Cache results or use loops.

---

## 11. Async/Await (and why to avoid it on the game thread)

```csharp
// C# async/await (similar to TS) — fine for loading screens, HTTP, file I/O off-thread
async Task<string> FetchLeaderboardAsync()
{
    var response = await httpClient.GetAsync(url);
    return await response.Content.ReadAsStringAsync();
}

// BUT ON THE MAIN/GAME THREAD: avoid async for gameplay flow
// Most engines require mutation (transforms, spawning, physics) on one thread

// Prefer: events / callbacks
public event Action<string>? DataLoaded;

public void LoadData()
{
    var data = ParseFile(path);
    DataLoaded?.Invoke(data);
}

// Prefer: frame-delayed callbacks (engine timer, next-tick queue)
public void WaitThenDo(float seconds, Action callback)
{
    _pendingCallbacks.Enqueue((Time + seconds, callback));
}

// Prefer: state machines over await-in-Update
// BAD: async void in per-frame hook — ordering and re-entrancy bugs
// GOOD: explicit states (Loading, Playing, Dying) checked each tick
```

---

## 12. Enums

```csharp
// Basic enum (backed by int)
public enum CharacterPose
{
    Standing,   // 0
    Crouching,  // 1
    Proning     // 2
}

// Explicit values
public enum WeaponType
{
    Pistol = 1,
    Shotgun = 2,
    Rifle = 10
}

// Flags enum (bitmask)
[Flags]
public enum PlayerState
{
    None = 0,
    Moving = 1,
    Jumping = 2,
    Crouching = 4,
    Shooting = 8
}

// Usage
var state = PlayerState.Moving | PlayerState.Shooting;
if (state.HasFlag(PlayerState.Moving)) { }
```

---

## 13. Generics

```csharp
// Generic class
public class NetworkInterpolator<T> where T : INetworkState
{
    private readonly Queue<T> _buffer = new();

    public void AddState(T state) => _buffer.Enqueue(state);
    public T? GetInterpolated(long timestamp) { /* ... */ }
}

// Generic method
public T Clamp<T>(T value, T min, T max) where T : IComparable<T>
{
    if (value.CompareTo(min) < 0) return min;
    if (value.CompareTo(max) > 0) return max;
    return value;
}

// Common constraints
where T : struct              // Must be value type
where T : class               // Must be reference type
where T : new()               // Must have parameterless constructor
where T : SomeBaseClass       // Must inherit from class
where T : ISomeInterface      // Must implement interface
where T : struct, Enum        // Must be an enum
```

---

## 14. Interfaces

```csharp
// Define contract
public interface IInteractable
{
    float InteractionDistance { get; }
    bool CanInteract(PlayerController player);
    void Interact(PlayerController player);
}

// Implement
public class WeaponPickup : IInteractable
{
    public Vector3 Position { get; set; }
    public float InteractionDistance => 1.25f;

    public bool CanInteract(PlayerController player)
    {
        var distance = Vector3.Distance(Position, player.Position);
        return distance <= InteractionDistance;
    }

    public void Interact(PlayerController player)
    {
        player.EquipWeapon(this);
        MarkForRemoval();  // return to pool or despawn — engine-specific
    }
}

// Default interface methods (C# 8+)
public interface INetworkState
{
    long Timestamp { get; }
    bool IsExpired(long currentTime, long maxAge = 1000)
        => currentTime - Timestamp > maxAge;
}
```

---

## 15. Attributes

```csharp
// Serialization / editor metadata — attribute names differ by engine
public class PlayerConfig
{
    // Editor-exposed range hint (exact attribute varies by engine)
    public int MaxHealth = 100;

    public float MoveSpeed = 5f;
}

// Your own attributes for cross-cutting concerns
[AttributeUsage(AttributeTargets.Method)]
public class RateLimitedAttribute(int maxPerSecond) : Attribute
{
    public int MaxPerSecond { get; } = maxPerSecond;
}

[RateLimited(30)]
public void SendInputToServer(InputSnapshot input) { }
```

---

## 16. Extension Methods

```csharp
using System.Numerics;

// Add methods to existing types
public static class Vector3Extensions
{
    public static Vector3 Flattened(this Vector3 v)
        => new(v.X, 0, v.Z);

    public static bool IsNearlyZero(this Vector3 v, float epsilon = 0.001f)
        => v.LengthSquared() < epsilon * epsilon;
}

// Usage (looks like instance method!)
Vector3 velocity = new(1, 5, 3);
Vector3 flatVelocity = velocity.Flattened();  // (1, 0, 3)
if (velocity.IsNearlyZero()) { }
```

---

## 17. Spans and Memory (Advanced)

```csharp
// Span<T>: A view into contiguous memory (like a slice)
// - No allocations
// - Stack-only (can't store in fields)
// - Great for parsing, serialization

public void ProcessData(Span<byte> data)
{
    // Slice without copying
    var header = data[..4];
    var payload = data[4..];

    // Modify in place
    data[0] = 0xFF;
}

// ReadOnlySpan: Immutable view
public int CountOnes(ReadOnlySpan<byte> data)
{
    int count = 0;
    foreach (var b in data)
        if (b == 1) count++;
    return count;
}

// Usage
byte[] array = [1, 2, 3, 4, 5];
ProcessData(array);  // Implicit conversion to Span
ProcessData(array.AsSpan(1, 3));  // Slice: [2, 3, 4]

// stackalloc: Allocate on stack (no GC)
Span<byte> buffer = stackalloc byte[64];
```

**`ref struct`:** like `Span<T>`, a type that must live on the stack and **cannot be boxed**. Use for short-lived serialization/parsing helpers. Full treatment in §24.

---

## 18. Records (C# 9+)

```csharp
// Record class: Immutable reference type with value equality
public record PlayerData(string Name, int Score);

var p1 = new PlayerData("Bob", 100);
var p2 = new PlayerData("Bob", 100);
Console.WriteLine(p1 == p2);  // true! (value equality)

// With expression: Non-destructive mutation
var p3 = p1 with { Score = 200 };  // New instance with changed score

// Record struct: Value type version
public readonly record struct Vector2Int(int X, int Y);
```

---

## 19. Primary Constructors (C# 12)

```csharp
// OLD WAY
public class Player
{
    private readonly string _name;
    private readonly int _maxHealth;

    public Player(string name, int maxHealth)
    {
        _name = name;
        _maxHealth = maxHealth;
    }
}

// NEW WAY: Primary constructor
public class Player(string name, int maxHealth)
{
    public string Name => name;
    public int MaxHealth => maxHealth;
    private int _health = maxHealth;
}

// Works for structs too
public readonly struct Damage(int amount, DamageType type)
{
    public int Amount => amount;
    public DamageType Type => type;
}
```

---

## 20. File-Scoped Namespaces (C# 10)

```csharp
// OLD WAY
namespace Game.Player
{
    public class PlayerController
    {
        // Everything indented
    }
}

// NEW WAY: One less indent level
namespace Game.Player;

public class PlayerController
{
    // Cleaner!
}
```

---

## 21. Game Loop Integration

Engines expose lifecycle hooks with different names; the **C# patterns** are the same:

| Concern | What to do (names vary by engine) |
|---------|-----------------------------------|
| One-time init | Wire dependencies, subscribe to events |
| Per-frame (variable dt) | Animation, UI, camera follow |
| Fixed simulation step | Physics, authoritative movement, networking tick |
| Teardown | Unsubscribe events, release pooled resources |

```csharp
public class PlayerController
{
    private InputSystem _input = null!;  // wired at init

    public void Initialize(InputSystem input)
    {
        _input = input;
        _input.JumpPressed += OnJump;
    }

    public void Update(float deltaTime)
    {
        // Visuals, animation, non-physics logic
        UpdateAnimations(deltaTime);
    }

    public void FixedUpdate(float fixedDelta)
    {
        // Physics, authoritative movement
        ApplyMovement(fixedDelta);
    }

    public void Shutdown()
    {
        _input.JumpPressed -= OnJump;  // always unsubscribe
    }
}
```

### Events vs polling

Prefer **events** for discrete actions (jump pressed, died, item picked up). **Poll** continuous state (movement axes) in the tick that consumes it.

```csharp
public class Health
{
    public event Action<int>? Changed;
    public event Action? Died;

    private int _value = 100;

    public void TakeDamage(int amount)
    {
        _value -= amount;
        Changed?.Invoke(_value);
        if (_value <= 0) Died?.Invoke();
    }
}
```

### Multiplayer roles (engine-neutral)

Most action games split work by **role**, not by file layout:

| Role | Responsibility |
|------|----------------|
| **Server / host** | Authoritative simulation, validates input |
| **Owning client** | Sends input, may predict locally |
| **Other clients** | Interpolate remote state, no authority |

Pick **reliable** delivery for one-shot events (spawn, pickup). Pick **unreliable** for high-frequency state where the next packet replaces the last (input, transform snapshots). Wrong channel choice shows up only on lossy internet — see your engine's networking docs for API names.

---

## 22. Common Gotchas

### Float vs double in delta time

Many engines pass **`double`** delta time from the loop but game math uses **`float`**. Cast explicitly when mixing:

```csharp
public void FixedUpdate(double delta)
{
    velocity += gravity * (float)delta;
}
```

### String Comparison

```csharp
// Ordinal comparison (fast, case-sensitive)
if (name == "Player") { }

// Case-insensitive
if (name.Equals("player", StringComparison.OrdinalIgnoreCase)) { }

// Don't use == for case-insensitive!
if (name.ToLower() == "player") { }  // BAD: allocates new string
```

### Event Unsubscription

```csharp
public class PlayerLifecycle
{
    private Health _health = null!;

    public void Initialize(Health health)
    {
        _health = health;
        _health.Died += OnPlayerDied;
    }

    public void Shutdown()
    {
        _health.Died -= OnPlayerDied;
    }
}
```

### Stale object references

```csharp
// Pooled or destroyed entities may still be referenced
if (target is { IsAlive: true })
{
    target.TakeDamage(10);
}
```

---

## 23. Performance Tips

Quick checklist before §24's deep dive:

```csharp
// 1. Avoid allocations in Update / FixedUpdate
private readonly List<Entity> _nearbyScratch = new();

public void FixedUpdate(float delta)
{
    _nearbyScratch.Clear();
    FindNearby(_nearbyScratch);
}

// 2. Use struct for frequently created data
public readonly struct HitInfo(Vector3 point, Vector3 normal, Entity collider);

// 3. Cache service / component references at init — not every frame
private Camera _camera = null!;

// 4. Cache frequent string keys (intern, static readonly, engine fast-string type)
private static readonly string JumpAction = "jump";

// 5. Avoid LINQ in hot paths — see §10
for (int i = 0; i < players.Count; i++)
{
    if (players[i].Health > 0) { /* ... */ }
}

// 6. Object pooling for frequently spawned types
// Instead of: new Bullet() + destroy
// Use: BulletPool.Rent() + BulletPool.Return(bullet)
```

---

## 24. Game Development: Performance-Oriented C#

This section ties together patterns from §3–§7 and §17 for **real-time and networked game code**. Idiomatic C# in games is not the same as idiomatic C# in ASP.NET — hot paths favor imperative loops and value types over LINQ and heap allocations.

### Hot path vs cold path

| Cold path (LINQ/allocations OK) | Hot path (imperative, zero/low alloc) |
|---------------------------------|---------------------------------------|
| Menu / lobby UI rebuild | Per-frame update (`Update`, tick callback) |
| One-time level load | Fixed-tick simulation (physics, networking) |
| Editor tooling | Serialization every network tick |
| Config / CLI parsing | Interpolation buffer trimming |

**Rule:** if it runs every frame or every network tick, avoid LINQ, boxing, and `new` on collections.

### Go-to data structures

```csharp
// Network DTOs — readonly struct with explicit wire format
public readonly struct InputSnapshot
{
    public readonly float MoveX;
    public readonly float MoveY;
    public readonly bool Jump;
    public long Timestamp { get; }

    public byte[] ToBytes() { /* PacketWriter */ }
    public static InputSnapshot FromBytes(ReadOnlySpan<byte> data) { /* PacketReader */ }
}

// Active entities — Dictionary keyed by stable ID
private readonly Dictionary<int, Entity> _entities = new();

// Pending messages / spawn-race buffer — Queue per key
private readonly Dictionary<int, Queue<(byte[] Payload, long EnqueuedAt)>> _pending = new();

// Membership without LINQ — HashSet
private readonly HashSet<int> _occupiedSpawnSlots = new();

// Reused scratch list — Clear() instead of new List every frame
private readonly List<Entity> _nearbyScratch = new();

public void FixedUpdate(float delta)
{
    _nearbyScratch.Clear();
    FindNearby(_nearbyScratch);
    for (int i = 0; i < _nearbyScratch.Count; i++)
    {
        // ...
    }
}
```

**`ArrayPool<byte>`** — when snapshot buffers allocate on every broadcast, rent a buffer from the pool, write into it, send, then return. Avoids GC spikes when many entities replicate state each tick.

### Go-to primitives

#### `ref struct` — stack-scoped, cannot be boxed

Regular `struct` can be **boxed** (wrapped in a heap `object`) when cast to `object`, assigned to an interface, or stored in non-generic collections. Boxing triggers GC.

`ref struct` (C# 7.2+) is **forbidden** from boxing and heap escape by the compiler:

- Cannot implement interfaces
- Cannot be a field on a class (except inside another `ref struct`)
- Cannot be used in `async`/`yield` methods
- Cannot be stored in `object` variables

```csharp
// Short-lived serialization helper — lives on the stack as a local
public ref struct PacketWriter
{
    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;

    public PacketWriter(int initialCapacity = 128)
    {
        _stream = new MemoryStream(initialCapacity);
        _writer = new BinaryWriter(_stream);
    }

    public void Write(float value) => _writer.Write(value);
    public void Write(int value) => _writer.Write(value);

    public byte[] ToArray()
    {
        _writer.Flush();
        return _stream.ToArray();
    }
}

// Usage: stack-local, discarded after ToBytes()
var writer = new PacketWriter(estimatedSize: 64);
writer.Write(snapshot.MoveX);
writer.Write(snapshot.MoveY);
return writer.ToArray();
```

**Honest caveat:** `PacketWriter` is stack-local but still allocates `MemoryStream`/`BinaryWriter` on the heap. The win is scoped lifetime and **no boxing of the wrapper** — not zero heap allocation overall. A further optimization is writing directly into a rented `ArrayPool` buffer with `BinaryPrimitives` or `Span<byte>`.

#### `Span<T>` / `ReadOnlySpan<T>` — slice without copy

See §17. Use for parsing byte buffers, string slices, and `stackalloc` views. `Span<T>` is itself a `ref struct`.

#### `stackalloc` — small fixed buffers on the stack

```csharp
Span<byte> header = stackalloc byte[16];
BinaryPrimitives.WriteInt32LittleEndian(header, value);
```

#### `in` / `ref` — avoid copying large structs

See §7. Pass large value types (transforms, bounding boxes, physics state) by `in` when read-only.

#### `BinaryPrimitives` — endian-safe primitives

Prefer over manual bit-shifting when reading/writing wire format without `BinaryWriter`.

#### Monotonic time for networking

Use a **monotonic** clock for network timestamps, latency measurement, and buffer TTLs — one that never jumps backward:

```csharp
// Pure .NET — monotonic, milliseconds since an arbitrary epoch
long now = Environment.TickCount64;

// Higher resolution — Stopwatch ticks converted to milliseconds
long nowMs = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp()).Milliseconds;
```

Game engines also expose monotonic clocks — use those for in-engine timing when available. **Do not** use Unix wall clock or `DateTime.UtcNow` for sync/sequencing — NTP, DST, and sleep/wake can jump time backward.

### Boxing explained

Boxing happens when a value type is treated as a reference type:

```csharp
object boxed = 42;             // int boxed on heap
IComparable c = 42;            // int boxed
ArrayList list = new() { 1 };  // each int boxed

// Enum boxing — common in serialization
object o = MyEnum.Value;                    // boxes enum
writer.Write((int)(object)value);           // bad path
```

**Avoid enum boxing in hot serialization:**

```csharp
// Reinterpret bits — no boxing
_writer.Write(Unsafe.As<TEnum, int>(ref value));
```

### Anti-patterns for games

| Avoid | Prefer | Why |
|-------|--------|-----|
| `.Where().Select().ToList()` in update loop | `for` loop over pre-sized `List<T>` | LINQ allocates enumerators + closures |
| `async/await` on the main/game thread | Callbacks, events, engine tick hooks | Most engines require main-thread mutation |
| `record` for wire-format types | `readonly struct` + explicit serialize | Explicit layout, no synthesized equality overhead |
| String concat in loops | Log interpolation sparingly; cache frequent strings | Reduces allocations |
| Unix / wall-clock time for network state | Monotonic clock (`TickCount64`, engine API) | Clock can jump backward |
| `as T` when type is expected | `is T` pattern + log on mismatch | Surfaces bugs instead of silent null |

### Quick reference: use this / not that

| Situation | Use | Not |
|-----------|-----|-----|
| Network snapshot payload | `readonly struct` + binary serializer | `class` or `record` |
| Serialize helper local | `ref struct PacketWriter` | `class SerializerHelper` |
| Slice byte buffer | `ReadOnlySpan<byte>` | `data.CopyTo(new byte[...])` |
| Buffer TTL / sequencing | Monotonic clock | `DateTimeOffset.UtcNow` |
| Menu / settings UI | `foreach` or LINQ (cold path) | — |
| Simulation / network tick | `for`, reused collections | LINQ |
| Enum on wire | `Unsafe.As<TEnum, int>` | `(object)enumValue` |
| Large struct parameter | `in Matrix4x4` (or engine transform) | pass by value copy |

---

## Quick Reference Table

| TypeScript | C# Equivalent |
|------------|---------------|
| `let x = 5` | `var x = 5;` |
| `const X = 5` | `const int X = 5;` |
| `x ?? y` | `x ?? y` |
| `x?.foo` | `x?.Foo` |
| `x!` | `x!` |
| `as Type` | `as Type` or `is Type` |
| `interface` | `interface` |
| `type Alias = ...` | No direct equivalent (use interface/class) |
| `enum` | `enum` |
| `{ ...obj }` | `obj with { }` (records only) |
| `array.map(x => ...)` | `array.Select(x => ...)` |
| `array.filter(x => ...)` | `array.Where(x => ...)` |
| `array.find(x => ...)` | `array.FirstOrDefault(x => ...)` |
| `array.some(x => ...)` | `array.Any(x => ...)` |
| `array.every(x => ...)` | `array.All(x => ...)` |
| `async/await` | `async/await` (avoid on game main thread) |
| `export class` | `public class` |
| `private` | `private` |
| `readonly` | `readonly` |
| `static` | `static` |

---

## Summary

1. **Know value vs reference types**—biggest perf impact
2. **Use `readonly struct` for immutable data**
3. **Use `ref struct` for stack-scoped helpers** (serialization, parsing) — cannot be boxed
4. **Hot path = imperative loops; cold path = LINQ is fine** (see §24)
5. **Enable nullable reference types in `.csproj`** — use `= null!` when init is engine-driven but invisible to the compiler
6. **Use pattern matching**—cleaner than if/else chains
7. **Prefer events/state machines over async on the game thread**
8. **Avoid allocations in hot paths**—reuse collections, use structs
9. **Always validate network inputs on server**
10. **Use `in` for large structs to avoid copies**
11. **Cache references at init**, not per frame
12. **Use a monotonic clock for network timing**, not Unix wall clock
13. **File-scoped namespaces + primary constructors = less boilerplate**
