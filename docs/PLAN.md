# Homelab Control Center: Architecture and Build Plan

Oct 19, 2026 · @michael

## What we're building

A custom web dashboard for your whole home setup: cluster health, Pi-hole stats, service uptime, and Home Assistant controls, all live, in one UI you designed. It runs on your k3s cluster and deploys itself whenever you push code.

It's built from three layers, each showing a different skill:

- **Front end:** a React + TypeScript app (the part you see). Your strongest area, so it should feel familiar.
- **Back end:** an ASP.NET Core API in C# that gathers data from your services and streams live updates. Also familiar ground from work.
- **Platform:** containers, Kubernetes manifests, a CI pipeline that builds images, and GitOps that deploys them. This is the new part, and the part that makes the project stand out.

Why it works as a portfolio piece: most developer portfolios stop at "deployed to Vercel." This one shows you can build the app *and* run the infrastructure under it, and it solves a real problem you have, which makes for a better interview story than a tutorial project.

## Architecture

&#91;embedded content: Dashboard architecture · the request path and the deploy path\]

Everything inside the shaded box runs on your Pis. The highlighted pod is the app you build; everything else already exists or gets installed.

**When you open `dash.lan`:**

1. Your phone asks Pi-hole where `dash.lan` is, and Pi-hole answers with RP1's IP. Away from home, this goes through Tailscale.
2. Traefik on RP1 reads the hostname and forwards the request to the dashboard's Service, which passes it to the pod.
3. The pod sends back the React files. The app loads and opens a SignalR connection back to the API.
4. The poller has already been fetching from Kubernetes, Uptime Kuma, Pi-hole, and Home Assistant. The API sends the latest snapshot, then pushes each change as it happens.
5. When you tap a light toggle, React calls your API, and the API calls Home Assistant with its token.

**When you push code:**

1. GitHub Actions runs the tests, builds the arm64 image, pushes it to GHCR, and commits the new image tag into `deploy/`.
2. Argo CD notices the commit and updates the Deployment to the new tag.
3. Kubernetes pulls the new image from GHCR and swaps the old pod for the new one.

## How each piece works

### The React front end

A Vite + React + TypeScript app. When built, it becomes plain static files (HTML, JavaScript, CSS) that any web server can hand to a browser.

The key rule: **the browser only ever talks to your API**, never directly to Pi-hole or Home Assistant. Two reasons. Your API passwords would be visible to anyone who opened the browser's dev tools. And browsers block a page from calling other sites' APIs unless those sites allow it (CORS), which Pi-hole and friends don't.

### The .NET API (a "backend for frontend")

An ASP.NET Core minimal API. Its job is to hold the secrets, call each service, and reshape everything into one clean format the UI wants. This pattern is called a backend for frontend, or BFF: an API that exists to serve one specific UI.

Three parts inside it:

- **Connectors:** one class per data source (Kubernetes, Pi-hole, Kuma, Home Assistant), each behind a shared interface like `IDataSource`. Adding a new source later means writing one new class.
- **A background poller:** a `BackgroundService` that asks each source for fresh data every few seconds and keeps the latest snapshot in memory. Polling happens once on the server, so five open browser tabs don't mean five times the load on your Pi-hole.
- **Static file hosting:** the API also serves the built React files. One container holds the whole app, which keeps deployment simple.

### The data sources

| Source | How the API talks to it | How it authenticates |
| --- | --- | --- |
| Kubernetes | The official `KubernetesClient` NuGet package | Inside the cluster, the pod's ServiceAccount token is picked up automatically; a read-only ClusterRole limits what it can see (same idea as Homepage's) |
| Pi-hole v6 | Its REST API | Log in with the app password to get a session ID, then send it on each request |
| Uptime Kuma | The JSON endpoints behind your `home` status page | None needed for a public status page |
| Home Assistant | Its REST API: read states, call services (like `light.turn_on`) | A long-lived access token as a Bearer header |

All passwords and tokens live in a Kubernetes **Secret** and reach the API as environment variables, never in your code or Git.

### Live updates with SignalR

Normally a browser has to ask the server for new data (polling). SignalR, Microsoft's real-time library, keeps a WebSocket connection open so the **server pushes** changes the moment the poller sees them. A light switched off in Home Assistant shows up on your dashboard within a second or two, with no refresh. The React side uses the `@microsoft/signalr` client package.

### The container

A multi-stage Dockerfile builds everything in three steps:

1. A Node image builds the React app.
2. A .NET SDK image compiles the API.
3. A small ASP.NET runtime image receives only the finished output from steps 1 and 2.

The final image contains no build tools, so it's smaller, faster to pull onto a Pi, and has less that could be exploited.

One catch: your Pis use **arm64** processors, and GitHub's build servers are **x86**. A container built for one won't run on the other. Docker's `buildx` tool builds for both architectures from one command, so the same image works on your Pis and on any cloud server.

### The Kubernetes resources

The same building blocks as your Uptime Kuma and Homepage manifests, plus a Secret:

| Resource | Its job in this project |
| --- | --- |
| Namespace | A `dashboard` folder grouping everything |
| ServiceAccount + ClusterRole | Read-only access to cluster info |
| Secret | Pi-hole password and Home Assistant token |
| Deployment | Runs the container, restarts it if it crashes |
| Service | A stable internal address for the pod |
| Ingress | Routes `dash.lan` to the Service, with Homepage annotations so it shows up there too |

### CI with GitHub Actions

Every push to `main` triggers a workflow that runs your tests, builds the multi-arch image, and pushes it to GitHub Container Registry (GHCR), tagged with the commit's SHA. Its last step updates the image tag in the repo's deploy manifest. That commit is the handoff to the next piece.

### GitOps with Argo CD

So far you've deployed by running `kubectl apply` yourself. That's **push** deployment. GitOps flips it to **pull**: Argo CD runs inside your cluster, watches the `deploy/` folder in your repo, and whenever it changes, makes the cluster match.

This solves a real problem for a home cluster. GitHub's servers can't reach your Pis, since they're behind your router and only reachable over Tailscale. With GitOps they don't need to: GitHub only writes to Git, and your cluster reaches out to fetch the change. Other benefits:

- Git becomes the record of what's deployed and when.
- Rolling back is a `git revert`.
- No cluster passwords ever leave your house.

## Repo layout

One public GitHub repo (a monorepo) holds everything, so a reviewer sees the whole system in one place:

```
homelab-control-center/
├── web/                    React + TypeScript app (Vite)
│   └── src/
├── api/                    ASP.NET Core API
│   ├── Connectors/         one class per data source
│   ├── Hubs/               SignalR hub for live updates
│   └── Program.cs
├── api.Tests/              unit tests for the connectors
├── deploy/                 Kubernetes manifests (Argo CD watches this)
│   ├── rbac.yaml
│   ├── deployment.yaml
│   └── ingress.yaml
├── .github/workflows/
│   └── build.yaml          CI: test, build, push, bump image tag
├── Dockerfile              multi-stage build
└── README.md               architecture diagram + the story
```

Secrets never go in this repo. The Kubernetes Secret is created once by hand on the cluster, the same way you did for Homepage.

## Build plan

Seven phases, each ending with something working you can see. Every phase builds on the last, so don't skip ahead: phase 3 is where the infrastructure learning really starts, and it only makes sense once phases 1 and 2 work.

### Phase 1: Local skeleton

Create the repo, a Vite React app in `web/`, and an ASP.NET Core minimal API in `api/` with one `/api/health` endpoint. Configure Vite's dev proxy so the React app can call the API during development.

- **You'll learn:** monorepo structure, how a front end and API talk in development.
- **Done when:** the page in your browser shows "API OK," fetched from your own API.

### Phase 2: First real data (cluster nodes)

Add the `KubernetesClient` package and an `/api/cluster/nodes` endpoint. Locally, it reads the kubeconfig you copy from RP1, which works from anywhere thanks to Tailscale. Show each node as a card with its status, CPU, and memory.

- **You'll learn:** the Kubernetes API, and the difference between kubeconfig auth (your laptop) and in-cluster auth (a pod).
- **Done when:** your laptop shows RP1, RP2, and RP3 with live numbers.

### Phase 3: Containerize and deploy by hand

Write the multi-stage Dockerfile, build it for arm64 with `buildx`, and push it to GHCR manually. Write the `deploy/` manifests (RBAC, Deployment, Service, Ingress), `kubectl apply` them, and add `dash.lan` to Pi-hole.

- **You'll learn:** images and registries, multi-arch builds, RBAC, and how a pod authenticates to the cluster.
- **Done when:** `http://dash.lan` shows your nodes, on your phone too.

#### Phase 3 add-on: trusted HTTPS for every .lan app

Right now Traefik only serves `kuma.lan`, `home.lan` and `dash.lan` over plain HTTP, so `https://` on them fails. cert-manager fixes that for every app at once, using your own certificate authority (the same idea as Pi-hole's `tls_ca.crt`).

1. **Install cert-manager** on the cluster with its official manifest or Helm chart. It's a controller that watches for certificate requests and keeps certificates renewed.
2. **Create your home CA** with three resources: a self-signed `ClusterIssuer`, a `Certificate` with `isCA: true` that it signs (your root CA, stored as a Secret), and a second `ClusterIssuer` named `lan-ca` that issues certificates from that root.
3. **Request certificates from each Ingress.** Add the annotation `cert-manager.io/cluster-issuer: lan-ca` and a `tls:` block listing the host (for example `dash.lan`) and a secret name. cert-manager issues the certificate into that Secret, and Traefik serves it on port 443.
4. **Trust the CA once on each device:** export the root CA from its Secret, import it into Windows' Trusted Root Certification Authorities, and install it on your iPhone as a profile, then turn on full trust under Certificate Trust Settings.
5. **Optional: redirect HTTP to HTTPS** with a Traefik `redirectScheme` Middleware, so typing a bare `kuma.lan` always lands on the padlocked version.
6. **Roll it out to Kuma and Homepage too** by adding the same annotation and `tls:` block to their Ingresses.

- **You'll learn:** how TLS and certificate authorities work, Kubernetes controllers and custom resources (`ClusterIssuer`, `Certificate`), and Ingress TLS.
- **Done when:** `https://dash.lan`, `https://kuma.lan` and `https://home.lan` all show a padlock with no warning, on both your PC and your phone.

Keep the CA's private key Secret out of Git; it can sign certificates your devices will trust.

### Phase 4: More sources and live updates

Add connectors for Pi-hole and Uptime Kuma, then Home Assistant once the Pi 5 is running. Move polling into a `BackgroundService`, push changes over SignalR, and add a light toggle that calls Home Assistant through your API.

- **You'll learn:** the BFF pattern, different auth styles, WebSockets, and sending commands (not just reading data).
- **Done when:** switching a light in the Home Assistant app updates your dashboard without a refresh, and your dashboard's toggle works too.

### Phase 5: CI with GitHub Actions

Write `.github/workflows/build.yaml`: run tests, build the multi-arch image, push to GHCR tagged with the commit SHA, then commit the new tag into `deploy/deployment.yaml`.

- **You'll learn:** Actions workflows, cross-architecture builds, and registry auth with the built-in `GITHUB_TOKEN`.
- **Done when:** a push to `main` produces a new image in GHCR with no manual steps.

### Phase 6: GitOps with Argo CD

Install Argo CD on the cluster and create one Application pointing at the repo's `deploy/` folder. From now on, you never run `kubectl apply` for this app.

- **You'll learn:** reconciliation (the cluster continuously matching Git), drift detection, and rollback by `git revert`.
- **Done when:** you push code, and a few minutes later it's live on `dash.lan` without touching the cluster.

### Phase 7: Portfolio polish

- A README with the architecture diagram, screenshots or a GIF, and a short "why I built this"
- A CI status badge
- A 1 to 2 minute demo video, since the live app is only reachable on your network
- A write-up on your portfolio site about the hardest problem you solved
- A line explaining the security design: the dashboard is only reachable through Tailscale, so it never touches the public internet

**Done when:** someone who's never seen it understands what it is and how it works from the README in about a minute.

## Learning while you build

The goal is to finish able to explain every line in an interview, so use Claude Code as a tutor, not a code generator.

- **Write the new parts yourself.** Let Claude Code handle boilerplate you already understand (project scaffolding, CSS). Write the Kubernetes, Docker, and CI pieces by hand, with Claude Code explaining and reviewing.
- **Ask for hints before answers.** "I'm stuck on X, give me a hint" teaches more than "fix X."
- **Ask why, not just how.** When something breaks, paste the error and ask what it means before asking for the fix.
- **The interview test:** if you can't explain a line, stop and ask about it before moving on.
- **Keep a `LEARNINGS.md`** in the repo with problems you hit and how you solved them. It becomes your portfolio write-up almost for free.

A prompt to start each phase in Claude Code:

```
We're building the Homelab Control Center: a React + TypeScript front end and an ASP.NET Core API, deployed to my 3-node k3s cluster on Raspberry Pis (arm64).

We're on Phase [N]: [paste the phase description from the plan].

I want to learn this, not just finish it. So:
1. Before writing anything, explain the concepts this phase needs and why.
2. Let me write the core code myself. Give hints when I'm stuck, not full solutions, unless I ask.
3. Write boilerplate I already know, but explain anything new.
4. Review my code when I'm done, and point out what a senior engineer would change.
5. At the end, give me 3 questions to check I understood the phase.
```
