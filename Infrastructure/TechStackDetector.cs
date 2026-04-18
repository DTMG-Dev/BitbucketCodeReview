using System.Text;
using BitbucketCodeReview.Models.Diff;

namespace BitbucketCodeReview.Infrastructure;

/// <summary>
/// Inspects the changed files in a diff to detect which technology stacks are present,
/// then returns stack-specific code review guidelines to use as a fallback when the
/// repository does not have a CODE_REVIEW_GUIDELINES.md file.
/// </summary>
public sealed class TechStackDetector
{
    private readonly ILogger<TechStackDetector> _logger;

    // ── Extension → stack mappings ────────────────────────────────────────────

    private static readonly Dictionary<string, TechStack> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"]     = TechStack.DotNet,
        [".csproj"] = TechStack.DotNet,
        [".razor"]  = TechStack.DotNet,
        [".cshtml"] = TechStack.DotNet,
        [".tsx"]    = TechStack.TypeScriptReact,
        [".ts"]     = TechStack.TypeScriptReact,
        [".jsx"]    = TechStack.JavaScript,
        [".js"]     = TechStack.JavaScript,
        [".mjs"]    = TechStack.JavaScript,
        [".cjs"]    = TechStack.JavaScript,
        [".py"]     = TechStack.Python,
        [".go"]     = TechStack.Go,
        [".java"]   = TechStack.Java,
        [".kt"]     = TechStack.Kotlin,
        [".kts"]    = TechStack.Kotlin,
        [".rs"]     = TechStack.Rust,
        [".sql"]    = TechStack.Sql,
        [".yaml"]   = TechStack.Config,
        [".yml"]    = TechStack.Config,
    };

    public TechStackDetector(ILogger<TechStackDetector> logger) => _logger = logger;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyses <paramref name="files"/> and returns tailored review guidelines.
    /// Multiple stacks in one PR produce combined guidelines (e.g. a full-stack repo).
    /// </summary>
    public string BuildFallbackGuidelines(IReadOnlyList<DiffFile> files)
    {
        // Count files per stack so we can rank them by prevalence
        var counts = new Dictionary<TechStack, int>();
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.FilePath);
            if (ExtensionMap.TryGetValue(ext, out var stack))
                counts[stack] = counts.GetValueOrDefault(stack) + 1;
        }

        if (counts.Count == 0)
        {
            _logger.LogInformation("Tech stack: unrecognised — applying general guidelines");
            return GeneralGuidelines();
        }

        // Most-prevalent stacks first
        var detected = counts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        _logger.LogInformation(
            "Tech stack detected: {Stacks} — applying stack-specific fallback guidelines",
            string.Join(", ", detected));

        var sb = new StringBuilder();
        sb.AppendLine("The following guidelines were automatically inferred from the file types changed in this PR.");
        sb.AppendLine("Apply them as the primary review criteria.");
        sb.AppendLine();

        foreach (var stack in detected)
            sb.AppendLine(GetStackGuidelines(stack));

        return sb.ToString();
    }

    // ── Per-stack guidelines ──────────────────────────────────────────────────

    private static string GetStackGuidelines(TechStack stack) => stack switch
    {
        TechStack.DotNet          => DotNetGuidelines,
        TechStack.TypeScriptReact => TypeScriptReactGuidelines,
        TechStack.JavaScript      => JavaScriptGuidelines,
        TechStack.Python          => PythonGuidelines,
        TechStack.Go              => GoGuidelines,
        TechStack.Java            => JavaGuidelines,
        TechStack.Kotlin          => KotlinGuidelines,
        TechStack.Rust            => RustGuidelines,
        TechStack.Sql             => SqlGuidelines,
        TechStack.Config          => ConfigGuidelines,
        _                         => GeneralGuidelines()
    };

    // ── .NET / C# ─────────────────────────────────────────────────────────────
    private const string DotNetGuidelines = """
        ## .NET / C# Guidelines

        **Async / Await**
        - Flag `async void` methods — they swallow exceptions; use `async Task` instead.
        - Flag `.Result`, `.Wait()`, and `.GetAwaiter().GetResult()` on Tasks — they deadlock in ASP.NET contexts.
        - Flag missing `CancellationToken` parameters on I/O methods.
        - Flag `ConfigureAwait(false)` absent in library code (required to avoid context-switch overhead).

        **Null Safety**
        - Flag any dereference of a nullable reference type without a null check.
        - Flag `!` (null-forgiving operator) without a clear justification comment.
        - Prefer `is null` / `is not null` over `== null` for correctness with overloaded operators.

        **Resource Management**
        - Flag `IDisposable` / `IAsyncDisposable` objects not wrapped in `using` or `await using`.
        - Flag `HttpClient` instantiated directly with `new` — it should be injected or created via `IHttpClientFactory`.
        - Flag `DbContext` created with `new` — it must be resolved from DI.

        **LINQ & Performance**
        - Flag multiple enumeration of an `IEnumerable<T>` — call `.ToList()` or `.ToArray()` once.
        - Flag `Count()` on an `ICollection` where `.Count` property is available.
        - Flag `.ToList()` inside loops or LINQ chains where not necessary.
        - Flag `async` lambdas passed to non-async LINQ methods (`.Select(async x => ...)` without `.WhenAll`).

        **Exception Handling**
        - Flag empty `catch` blocks or `catch` that only rethrows without logging.
        - Flag catching `Exception` or `SystemException` as a blanket catch — be specific.
        - Flag missing `ex` parameter logging in catch blocks that swallow the exception.

        **Security**
        - Flag string-interpolated SQL queries — use parameterised queries or an ORM.
        - Flag hardcoded credentials, connection strings, or API keys.
        - Flag missing `[Authorize]` on controller actions that expose sensitive data.
        - Flag user-supplied input used directly in file paths (`Path.Combine` with unvalidated input).

        **Dependency Injection**
        - Flag `IServiceProvider.GetService<T>()` (service locator anti-pattern) except in factory scenarios.
        - Flag registering a scoped service as a singleton — it will capture a stale scope.
        - Flag constructor with more than ~5 parameters — consider refactoring responsibilities.

        **Entity Framework**
        - Flag loading entities with `.Include()` chains deeper than 3 levels — consider projections.
        - Flag missing `AsNoTracking()` on read-only queries in controllers/services.
        - Flag `SaveChangesAsync()` inside loops — batch saves outside the loop.
        """;

    // ── TypeScript / React ────────────────────────────────────────────────────
    private const string TypeScriptReactGuidelines = """
        ## TypeScript / React Guidelines

        **Type Safety**
        - Flag `any` type — prefer `unknown` with type narrowing or a specific type.
        - Flag type assertions (`as SomeType`) without a guard comment explaining why it is safe.
        - Flag `@ts-ignore` and `@ts-expect-error` without an explanation comment.
        - Flag functions that return `void` but are used as if they return a value.

        **React Hooks**
        - Flag `useEffect` with a missing or incomplete dependency array — list all variables from the enclosing scope.
        - Flag state mutations inside `useEffect` that could cause infinite re-render loops.
        - Flag stale closures capturing old state/props due to missing deps in `useCallback` / `useMemo`.
        - Flag direct array/object mutation of state (`state.items.push(...)`) — always create a new reference.
        - Flag `useEffect` used for data-fetching without a cleanup function to cancel in-flight requests.

        **Async & Promises**
        - Flag `async` functions in `useEffect` — they return a Promise; the cleanup must be synchronous.
        - Flag unhandled promise rejections (missing `.catch()` or try/catch around `await`).
        - Flag `await` inside loops that could be parallelised with `Promise.all`.

        **Security**
        - Flag `dangerouslySetInnerHTML` — if unavoidable, verify sanitisation with DOMPurify.
        - Flag user-provided URLs passed to `href` or `src` without validation (open redirect / XSS).
        - Flag storing sensitive data (tokens, PII) in `localStorage` or `sessionStorage`.

        **Performance**
        - Flag large components not wrapped in `React.memo` when they receive stable props.
        - Flag inline object/array literals passed as props — they create new references on every render.
        - Flag missing `key` prop or using array index as `key` in lists that can reorder.
        - Flag synchronous expensive computations in the render path — move to `useMemo`.

        **Accessibility**
        - Flag interactive elements (`div`, `span`) used as buttons without `role` and keyboard handlers.
        - Flag images missing meaningful `alt` text.
        - Flag form inputs without an associated `<label>` or `aria-label`.
        """;

    // ── JavaScript ────────────────────────────────────────────────────────────
    private const string JavaScriptGuidelines = """
        ## JavaScript Guidelines

        **Type Safety & Equality**
        - Flag `==` and `!=` — always use `===` and `!==` to avoid implicit coercion.
        - Flag `typeof` checks that should be using `instanceof` for objects.
        - Flag `NaN` compared with `==` or `===` — use `Number.isNaN()`.

        **Async & Promises**
        - Flag `async` functions whose return value (Promise) is not awaited by the caller.
        - Flag `.then()` chains without a terminal `.catch()`.
        - Flag `await` inside `.forEach()` — use `for...of` or `Promise.all` with `.map()`.
        - Flag missing error handling around `JSON.parse()` — it throws on invalid input.

        **Security**
        - Flag `eval()`, `new Function()`, and `innerHTML` assignments with unsanitised data.
        - Flag `document.write()` — it overwrites the entire document.
        - Flag user input passed directly to `setTimeout` or `setInterval` as a string.
        - Flag `require()` calls with dynamic, user-supplied paths.

        **Memory Leaks**
        - Flag event listeners added inside `useEffect` / component lifecycle without removal on cleanup.
        - Flag `setInterval` without a corresponding `clearInterval` in cleanup.
        - Flag closures that hold references to large DOM trees or data structures longer than needed.

        **Code Quality**
        - Flag `var` declarations — use `const` or `let`.
        - Flag functions longer than ~50 lines — suggest decomposing.
        - Flag deeply nested callbacks (callback hell) — suggest Promises or async/await.
        - Flag magic numbers and strings — extract to named constants.
        """;

    // ── Python ────────────────────────────────────────────────────────────────
    private const string PythonGuidelines = """
        ## Python Guidelines

        **Common Pitfalls**
        - Flag mutable default arguments (`def fn(x=[])`) — the default is shared across calls; use `None` and initialise inside.
        - Flag `except:` or `except Exception:` without re-raising or logging — swallowed exceptions hide bugs.
        - Flag comparing to `None` with `==` — use `is None` / `is not None`.
        - Flag `assert` used for runtime validation — assertions are stripped with `-O`; use explicit checks.

        **Security**
        - Flag f-string or `%`-formatted SQL queries — use parameterised queries with `?` or `%s` placeholders.
        - Flag `subprocess.call(..., shell=True)` with user-supplied input — command injection risk.
        - Flag `pickle.loads()` on untrusted data — it executes arbitrary code.
        - Flag `yaml.load()` without `Loader=yaml.SafeLoader` — use `yaml.safe_load()`.
        - Flag `eval()` or `exec()` on untrusted input.
        - Flag hardcoded credentials or API keys.

        **Async**
        - Flag `asyncio.run()` called inside a running event loop — use `await` instead.
        - Flag blocking I/O (`time.sleep`, `requests.get`) inside `async def` — use `asyncio.sleep` and an async HTTP client.

        **Type Hints**
        - Flag public functions/methods missing return type annotations.
        - Flag `Optional[T]` where `T | None` (Python 3.10+) is preferred.

        **Resource Management**
        - Flag file handles, database connections, or network sockets not managed with `with` statements.
        - Flag `threading.Lock` acquired without a `with` block (risk of deadlock on exception).

        **Performance**
        - Flag string concatenation in loops (`s += "..."`) — use `"".join(list)` instead.
        - Flag `list.insert(0, ...)` or `list.pop(0)` — use `collections.deque` for O(1) operations.
        """;

    // ── Go ────────────────────────────────────────────────────────────────────
    private const string GoGuidelines = """
        ## Go Guidelines

        **Error Handling**
        - Flag errors assigned to `_` — every error must be handled or explicitly ignored with a comment.
        - Flag `log.Fatal` / `os.Exit` inside library code — only acceptable in `main`.
        - Flag panic used for non-programmer errors — use error return values instead.

        **Goroutines & Concurrency**
        - Flag goroutines launched without a mechanism to wait for completion (`sync.WaitGroup`, channel, context).
        - Flag goroutines that could leak (no context cancellation, no done channel).
        - Flag `sync.Mutex` fields not passed by pointer (copying a mutex is a data race).
        - Flag shared mutable state accessed from multiple goroutines without synchronisation.
        - Flag channel operations that could deadlock (send/receive with no counterpart).

        **Context**
        - Flag I/O or long-running operations not accepting a `context.Context` parameter.
        - Flag `context.Background()` used deep in the call stack instead of propagating the parent context.
        - Flag `context.WithCancel` / `WithTimeout` where the `cancel` func is not deferred.

        **Resource Management**
        - Flag `defer` inside loops — defers accumulate; close resources explicitly inside the loop.
        - Flag HTTP response bodies not closed with `defer resp.Body.Close()`.
        - Flag file handles not closed after use.

        **Security**
        - Flag `fmt.Sprintf`-constructed SQL strings — use parameterised queries.
        - Flag `exec.Command` with user input — sanitise or use argument lists, never shell expansion.
        - Flag hardcoded credentials.

        **Code Quality**
        - Flag exported identifiers without a doc comment.
        - Flag functions returning more than 3 values — consider a struct.
        - Flag `interface{}` (or `any`) used where a concrete type would work.
        """;

    // ── Java ──────────────────────────────────────────────────────────────────
    private const string JavaGuidelines = """
        ## Java Guidelines

        **Null Safety**
        - Flag direct dereference of values that can be `null` without a null check or `Optional` usage.
        - Flag returning `null` from public methods — prefer `Optional<T>` or an empty collection.
        - Flag `Optional.get()` without `isPresent()` — use `orElse`, `orElseThrow`, or `ifPresent`.

        **Exception Handling**
        - Flag empty `catch` blocks — at minimum log the exception.
        - Flag catching `Exception` or `Throwable` without re-throwing or wrapping.
        - Flag checked exceptions swallowed and re-thrown as unchecked without preserving the cause.

        **Concurrency**
        - Flag shared mutable fields without `volatile` or synchronisation.
        - Flag `SimpleDateFormat` used as a shared static field — it is not thread-safe.
        - Flag `HashMap` used in a concurrent context — use `ConcurrentHashMap`.
        - Flag `synchronized` blocks on non-private objects — the lock can be acquired externally.

        **Performance**
        - Flag string concatenation with `+` inside loops — use `StringBuilder`.
        - Flag boxing/unboxing in tight loops (e.g., `Integer` in a collection).
        - Flag `List.contains()` / `Map.get()` on unsorted `ArrayList` when a `HashSet` / `HashMap` is appropriate.
        - Flag stream operations that re-iterate the source multiple times.

        **Security**
        - Flag `Statement.execute(userInput)` — use `PreparedStatement`.
        - Flag `ObjectInputStream.readObject()` on untrusted data — Java deserialization vulnerability.
        - Flag hardcoded passwords or tokens.
        - Flag `Runtime.exec(userInput)` — command injection.

        **Resource Management**
        - Flag resources (streams, connections, readers) not closed in a `try-with-resources` block.
        - Flag `finalize()` overrides — deprecated and unreliable; use `Cleaner` or `try-with-resources`.
        """;

    // ── Kotlin ────────────────────────────────────────────────────────────────
    private const string KotlinGuidelines = """
        ## Kotlin Guidelines

        **Null Safety**
        - Flag `!!` (not-null assertion) — replace with safe calls, `?:`, or proper null checks.
        - Flag platform types from Java interop used without explicit nullability annotation.
        - Flag `lateinit var` without an `isInitialized` guard where access before init is possible.

        **Coroutines**
        - Flag `GlobalScope.launch` — use a structured coroutine scope tied to a lifecycle.
        - Flag `runBlocking` inside a suspend function or on the main thread — it blocks the thread.
        - Flag `launch` / `async` without an explicit `CoroutineExceptionHandler` for fire-and-forget jobs.
        - Flag missing `withContext(Dispatchers.IO)` around blocking I/O inside a coroutine.
        - Flag `async { }.await()` called immediately — use `withContext` instead.

        **Sealed Classes / When**
        - Flag `when` expressions on sealed classes / enums missing an `else` branch that can miss new variants.

        **Collections**
        - Flag mutable collection types (`MutableList`, `MutableMap`) exposed in public APIs — expose read-only views.
        - Flag `ArrayList()` created explicitly where `mutableListOf()` is idiomatic.

        **Security & Resources**
        - Flag string-interpolated SQL — use parameterised queries.
        - Flag hardcoded credentials.
        - Flag `Closeable` resources not managed with `use { }`.
        """;

    // ── Rust ─────────────────────────────────────────────────────────────────
    private const string RustGuidelines = """
        ## Rust Guidelines

        **Panics & Error Handling**
        - Flag `.unwrap()` and `.expect()` in production paths — use `?` operator or proper `match`/`if let`.
        - Flag `panic!()` in library code — libraries should return `Result` or `Option`.
        - Flag `unreachable!()` in match arms without exhaustive proof — add a comment explaining why.

        **Unsafe Code**
        - Flag any `unsafe` block — verify the invariants are documented in a safety comment (`// SAFETY:`).
        - Flag raw pointer dereference without lifetime/aliasing analysis documented.
        - Flag `mem::transmute` — confirm types are ABI-compatible and document why.

        **Ownership & Lifetimes**
        - Flag cloning data inside hot paths that could use references instead.
        - Flag `Arc<Mutex<T>>` where `Rc<RefCell<T>>` suffices (single-threaded) or where ownership transfer would be cleaner.
        - Flag lifetime annotations that are overly restrictive — check if `'static` bounds are truly required.

        **Concurrency**
        - Flag `Mutex::lock().unwrap()` — a poisoned mutex will panic; consider `lock().unwrap_or_else(|e| e.into_inner())`.
        - Flag shared mutable state without synchronisation (only relevant in `unsafe` or with `UnsafeCell`).
        - Flag spawning threads that borrow non-`'static` data without using scoped threads.

        **Performance**
        - Flag unnecessary `Vec` allocations where a slice would do.
        - Flag `format!()` used purely for string concatenation — use `push_str` on a `String`.
        - Flag collecting into a `Vec` and then immediately iterating — chain the iterator instead.
        """;

    // ── SQL ───────────────────────────────────────────────────────────────────
    private const string SqlGuidelines = """
        ## SQL Guidelines

        **Security**
        - Flag any dynamic SQL built by string concatenation with user input — use parameterised queries.
        - Flag overly permissive `GRANT` statements (e.g., `GRANT ALL ON *.* TO user`).
        - Flag stored procedures or views that expose more data than necessary.

        **Correctness**
        - Flag `NULL` comparisons using `=` or `!=` — use `IS NULL` / `IS NOT NULL`.
        - Flag `NOT IN (subquery)` where the subquery can return `NULL` — the whole predicate evaluates to UNKNOWN; use `NOT EXISTS`.
        - Flag `UNION` where `UNION ALL` is intended (removes duplicates expensively).
        - Flag implicit type conversions in `JOIN` or `WHERE` conditions — they prevent index use.

        **Performance**
        - Flag missing indexes on columns used in `JOIN ON`, `WHERE`, or `ORDER BY` clauses of new queries.
        - Flag `SELECT *` in application code — list only needed columns.
        - Flag functions applied to indexed columns in `WHERE` (e.g., `WHERE YEAR(created_at) = 2024`) — they prevent index seek.
        - Flag correlated subqueries that run once per row — rewrite as a `JOIN` or CTE.
        - Flag large `IN (...)` lists — consider a temporary table or a `JOIN`.

        **Migrations**
        - Flag `DROP TABLE` or `DROP COLUMN` without a rollback strategy.
        - Flag adding a `NOT NULL` column without a default — this locks the table on large datasets.
        - Flag renaming a column or table without checking application code references.
        """;

    // ── YAML / Config ────────────────────────────────────────────────────────
    private const string ConfigGuidelines = """
        ## YAML / Configuration Guidelines

        **Security**
        - Flag plaintext secrets, passwords, or API keys — they must be injected via environment variables or a secrets manager.
        - Flag overly permissive CORS, IAM, or firewall rules (e.g., `0.0.0.0/0`, `*`).
        - Flag `privileged: true` or `runAsRoot: true` in container/Kubernetes specs.

        **Correctness**
        - Flag YAML boolean gotchas: `yes`, `no`, `on`, `off` are booleans in YAML 1.1 — quote them if string is intended.
        - Flag duplicate keys — later values silently override earlier ones.
        - Flag environment variable references (`${VAR}`) that are not documented or defaulted.

        **Best Practices**
        - Flag resource limits (`cpu`, `memory`) missing from container specs — unset limits can starve other services.
        - Flag health-check paths or timeouts that seem too aggressive or too lenient.
        - Flag configuration that differs unexpectedly between environments (dev vs. prod) without explanation.
        """;

    // ── General fallback ──────────────────────────────────────────────────────
    private static string GeneralGuidelines() => """
        ## General Code Review Guidelines

        Apply universal best practices across all languages:

        - **Security:** flag hardcoded secrets, unvalidated user input used in I/O or queries,
          and missing authentication/authorisation on sensitive operations.
        - **Error handling:** flag swallowed exceptions, empty catch blocks, and missing error
          logging in failure paths.
        - **Resource leaks:** flag open files, connections, or handles that are not explicitly closed.
        - **Null/nil safety:** flag unconditional dereferences of values that could be null/nil/None.
        - **Performance:** flag obvious N+1 patterns, string concatenation in loops, and
          unnecessary allocations in hot paths.
        - **Correctness:** flag off-by-one errors, incorrect boundary conditions, and logic
          that does not match the PR description.
        - **Maintainability:** flag magic numbers/strings, functions longer than ~50 lines,
          and deeply nested control flow.
        """;
}

// ── Tech stack enumeration ────────────────────────────────────────────────────

public enum TechStack
{
    DotNet,
    TypeScriptReact,
    JavaScript,
    Python,
    Go,
    Java,
    Kotlin,
    Rust,
    Sql,
    Config
}
