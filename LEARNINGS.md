# Learnings

Problems I hit while building the Homelab Control Center, what I got wrong, and how I fixed my understanding.

## To practice

Gaps from the Phase 1 review. Each one came up more than once, so each has a hands-on exercise in this repo. Remove an item once I can explain it without notes.

### 1. Proxy failure vs. CORS failure

**What I mixed up:** I thought "the API returned 200 but the page says *Failed to fetch*" meant the Vite proxy port was wrong. It doesn't. A wrong port means the request never reaches the API at all.

**The rule:** CORS is enforced by the **browser**, on the **response**. It doesn't stop a request from reaching the server. The server does the work, and the browser refuses to let my JavaScript read the answer.

| | Proxy points at the wrong port | CORS block |
| --- | --- | --- |
| Cause | `vite.config.ts` and `launchSettings.json` disagree on the port | `fetch` uses a full URL to another origin (e.g. `http://localhost:5080/...`) and the API sends no `Access-Control-Allow-Origin` header |
| Does the request reach the API? | **No** | **Yes**, and the API returns 200 |
| Page shows | `API unreachable: HTTP 500` (Vite's proxy error response) | `API unreachable: Failed to fetch` |
| Where to look | Vite terminal: `http proxy error ... ECONNREFUSED` | Browser console: `blocked by CORS policy` |

**Why our app needs no CORS setup:** the browser only ever talks to its own origin. In dev that's Vite (`:5173`), which forwards `/api/*` server-to-server to the API. In production the API serves the React files itself, so the page and the API share one origin (`dash.lan`).

#### Exercise

First, let the API log every request. In `api/appsettings.Development.json`, add this line inside `"LogLevel"`:

```json
"Microsoft.AspNetCore.Hosting.Diagnostics": "Information"
```

Then try both failures and fill in what you see:

1. **Wrong port.** In `web/vite.config.ts`, change `5080` to `5081`. Reload the page. Check the page, the Vite terminal, and the API terminal. Did the API log a request?
2. **CORS.** Change the port back. In `web/src/App.tsx`, change `fetch('/api/health', ...)` to `fetch('http://localhost:5080/api/health', ...)`. Reload. Check the page, the browser console, and the API terminal. Did the API log a request this time? What status did it return?

Undo everything when done:

```bash
git checkout -- web api
```

#### Check yourself

<details>
<summary>The API terminal shows <code>Request finished ... 200</code> but the page shows "Failed to fetch". What's wrong?</summary>

CORS. The request reached the API and succeeded, but the browser blocked the response because the page's origin differs from the API's. Look for a `fetch` using a full URL instead of a relative one.
</details>

<details>
<summary>The page shows "HTTP 500" and the API terminal shows nothing. What's wrong?</summary>

The proxy can't reach the API: the API is down or on a different port than `vite.config.ts` says. The 500 comes from Vite, not from the API.
</details>

<details>
<summary>Does CORS protect an endpoint from being called by other websites?</summary>

No. The request still reaches the server and runs. CORS only stops the browser from handing the response to the calling page. Endpoints that change things (like a light toggle) need real protection: authentication, or not being reachable at all.
</details>

---

### 2. Solution vs. project vs. assembly

**What I mixed up:** I said `partial` lets a class span multiple solutions, that `public` means "public to the solution", and that xUnit groups projects together. None of these is true.

| Term | What it is | In this repo |
| --- | --- | --- |
| **Solution** (`.slnx`) | A list of projects, so the IDE and `dotnet test` can open them together. **Has no effect on what code can see.** | One: `homelab-control-center.slnx` |
| **Project** (`.csproj`) | Builds into exactly one assembly. Declares its packages and its references to other projects. | Two: `api`, `api.Tests` |
| **Assembly** (`.dll`) | The compiled output. Access modifiers work at this level. | `api.dll`, `api.Tests.dll` |

**The rules:**

- `internal` means visible only inside the same **assembly**.
- `public` means visible to any assembly that **references** this one. To use a type from another project you need **both** a project reference (knowing the address) **and** the type being public (the door being unlocked).
- `partial` means "one class, defined in several pieces; merge them." All the pieces must be in the **same project**. It has nothing to do with other projects.
- xUnit is a **test framework** (a NuGet package). It has nothing to do with grouping projects.

**The `Program` story:** top-level statements make the compiler generate a class that is always named `Program`, whatever the file is called. Before .NET 10 it was `internal`, so `api.Tests` couldn't name it in `WebApplicationFactory<Program>`. The fix was `public partial class Program;`. Here `partial` means "add to the generated `Program`" and `public` is what gets added. Without `partial`, it's a second class with the same name and the build fails. .NET 10 generates this line for you, so we removed it.

#### Exercise

Stop the API dev server first, since it locks `api.dll` and the build will fail with "file is locked".

1. Open `homelab-control-center.slnx` in a text editor. Confirm it only lists the two projects and contains no code or settings.
2. Open `api.Tests/api.Tests.csproj` and find the `<ProjectReference>` line. Delete it and run `dotnet build`. Read the error: which type can't be found, and why?
3. Restore the line, then look in `api/bin/Debug/net10.0/` and find `api.dll`. That file is the assembly.
4. In `api/Program.cs`, add `internal class Secret { }` at the bottom. In the test file, write `var s = new Secret();`. Build. Read the error, then change `internal` to `public` and build again.

Undo everything when done:

```bash
git checkout -- api api.Tests
```

#### Check yourself

<details>
<summary>Fill in: "<code>api.Tests</code> can use <code>public</code> types in <code>api</code> because its ____ references <code>api</code>." / "<code>internal</code> means visible only inside the same ____." / "The ____ file groups projects for the IDE."</summary>

Project. Assembly. Solution.
</details>

<details>
<summary>A third project in the same solution does <em>not</em> reference <code>api</code>. Can it use the public <code>Program</code> class?</summary>

No. Without a project reference the compiler doesn't know `api.dll` exists. Being in the same solution grants nothing.
</details>

<details>
<summary>On .NET 8, someone writes <code>public class Program { }</code> at the bottom of <code>Program.cs</code>. What happens?</summary>

Build error. The compiler already generated a `Program` from the top-level statements, so this is a second definition of the same type. Adding `partial` fixes it by making both declarations pieces of one class.
</details>

---

### 3. Unit test vs. integration test

**What I mixed up:** I called a test that goes through `WebApplicationFactory` a unit test, and I thought a connector unit test would "share" an app.

**The rule:** if the test **starts the app** (`WebApplicationFactory`), it's an **integration test**. Real startup, dependency injection, routing and JSON serialization all run; only the web server (Kestrel) is swapped for an in-memory `TestServer`.

| | Unit test | Integration test |
| --- | --- | --- |
| Tests | One class on its own | Several real pieces together |
| Dependencies | Faked (e.g. a fake `HttpMessageHandler` returning saved JSON) | Real, except what you deliberately swap out |
| Starts the app? | **No**, so there's nothing to share | **Yes**, via `WebApplicationFactory` |
| Catches | Logic bugs in that class (parsing, error handling) | Wiring bugs: wrong route, endpoint not registered, startup crash, wrong JSON shape |
| Example | Pi-hole connector parsing a blocked-query count | `GET /api/health` returns 200 and `{"status":"OK"}` |

**Sharing the app (integration tests only):** xUnit creates a **new instance of the test class for every test**. So:

- `IClassFixture<WebApplicationFactory<Program>>` plus a constructor parameter: xUnit creates **one** app for the whole class and disposes it at the end. Use this when tests only read.
- `new WebApplicationFactory<Program>()` as a field: **one app per test**. The class must implement `IDisposable` and dispose it, or every test leaves a running app behind (and from Phase 4, a running poller). Use this when tests change state.
- In between: `factory.WithWebHostBuilder(...)` gives one test a customized copy of the shared app (e.g. with a fake Pi-hole that's down).

**Also:** `[Fact]` marks a test method. `[Theory]` + `[InlineData]` runs one test with several inputs. `[Fact(Skip = "reason")]` skips a test: its code never runs, and it shows in the `skipped:` count of the test run summary until I either finish it or delete it.

**xUnit v3:** this project uses `xunit.v3` (the old `xunit` v2 package is marked Legacy on NuGet). `[Fact]`, `[Theory]`, `Skip`, `Assert` and `IClassFixture` work the same. Differences: the test project builds as an executable (`<OutputType>Exe</OutputType>`), `dotnet test` runs it through Microsoft.Testing.Platform (opted in by `global.json` at the repo root), and each test gets `TestContext.Current.CancellationToken` to pass into async calls, the test-side version of the `CancellationToken` an endpoint gets.

#### Exercise

Write these myself in `api.Tests/HealthEndpointTests.cs`, then run `dotnet test`:

1. An integration test that `GET /api/does-not-exist` returns `404 NotFound`. Does it need a new app, or can it share the existing one?
2. Turn it into a `[Theory]` that checks two or three bad URLs.
3. Add `[Fact(Skip = "Phase 2")]` to a placeholder test for `/api/cluster/nodes`, run `dotnet test`, and read the summary line.

#### Check yourself

<details>
<summary>Unit or integration, and shared or fresh app? (a) <code>GET /api/health</code> returns 200. (b) Toggle a light, then check <code>GET /api/lights</code> shows it on. (c) The Pi-hole connector parses saved JSON from a fake handler.</summary>

(a) Integration, shared app (it only reads). (b) Integration, fresh app or a `WithWebHostBuilder` copy (it changes state). (c) Unit, and no app is started at all, so there's nothing to share.
</details>

<details>
<summary>How many times does the app start for a class with three <code>[Fact]</code>s and <code>IClassFixture</code>? Without <code>IClassFixture</code>, using a field <code>new()</code>?</summary>

Once. Three times, because xUnit creates a new class instance per test.
</details>

<details>
<summary>The summary says <code>succeeded: 4</code>, <code>skipped: 1</code>. Is everything fine?</summary>

Not necessarily. Whatever the skipped test covers is untested. Read its `Skip` reason, then either finish the work and remove `Skip`, or delete the test.
</details>

---

## Log

Problems hit while building, newest first.

### Phase 2: Cluster nodes

- **`xunit` v2 is Legacy; moved to `xunit.v3`.** On the .NET 10 SDK, v3 failed with "Testing with VSTest target is no longer supported" until `global.json` opted `dotnet test` into Microsoft.Testing.Platform. The new analyzer (xUnit1051) then asked for `TestContext.Current.CancellationToken` to be passed to `GetAsync` and `ReadFromJsonAsync`.

### Phase 1: Local skeleton

- **`public partial class Program;` is redundant on .NET 10.** ASP.NET Core 10 generates it automatically. We added it out of habit, confirmed the test passes without it, and removed it.
- **The dev API locks its build output on Windows.** Running `dotnet build` / `dotnet test` while `dotnet run` is active fails with "file is locked by api". Stop the API first, or build to a separate folder with `--artifacts-path`.
