# AI Assistant Cloud Backend

The Gemini side of the assistant: chat, screen analysis, embeddings, retrieval and the cloud
agent, served as one FastAPI service that runs on Cloud Run.

The desktop client never holds a Gemini credential. It talks to this service over HTTPS and this
service talks to Gemini with Application Default Credentials, so there is no long-lived secret in
the system to leak or rotate.

## What is here

| Path | What it is |
| --- | --- |
| `app/api/chat.py` | `POST /api/chat`, `POST /api/chat/stream` |
| `app/api/vision.py` | `POST /api/vision/analyze` |
| `app/api/embeddings.py` | `POST /api/embeddings` |
| `app/api/agents.py` | `POST /api/agent/run`, `GET /api/agent/tools` |
| `app/api/auth.py` | `POST /api/auth/token` (development only), `GET /health` |
| `app/services/gemini_service.py` | The only file that imports the Gemini SDK |
| `app/services/rag_service.py` | Retrieval, grounding, citation labelling |
| `app/services/vector_database.py` | The store contract and its two implementations |
| `app/services/agent_service.py` | Planner, executor, and the tool registry |
| `app/middleware/` | Tokens, rate limits, access logging |
| `benchmark.py` | Latency against the budgets the client assumes |

## Running it locally

Nothing below needs a Google Cloud project.

```powershell
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
Copy-Item .env.example .env
uvicorn app.main:app --reload
```

With no `GEMINI_API_KEY` and no `PROJECT_ID`, `/health` reports that the service has no way to
reach Gemini and every model call fails with a code the desktop client falls back on. That is
deliberate: the service starts, reports itself honestly, and the client's local capabilities keep
working.

To exercise the routes without a key, turn authentication off and use the development token
endpoint:

```powershell
$env:REQUIRE_AUTHENTICATION = "false"
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8000/api/auth/token `
  -ContentType application/json -Body '{"subject": "local-developer"}'
```

That endpoint returns 404 as soon as `REQUIRE_AUTHENTICATION` is on or `ENVIRONMENT` is
`production`, whichever comes first. A deployment that takes authentication seriously must not also
offer a way to mint a token for a caller-supplied subject.

## Tests

```powershell
python -m pytest              # 170 tests, no credentials, no network
python benchmark.py           # latency against the documented budgets
```

The whole suite runs against a scripted model. A test that needed a Gemini key would fail on
somebody else's machine and be skipped, and a skipped privacy test is worse than no privacy test.

## Configuration

Every setting is an environment variable, validated on load. See `.env.example` for the full list
with comments. The ones that decide how the service behaves:

| Variable | Effect |
| --- | --- |
| `GEMINI_API_KEY` | Local development. Leave empty in production. |
| `PROJECT_ID` | Selects Vertex AI with Application Default Credentials. |
| `VECTOR_STORE` | `in-memory` or `vertex`. Explicit, never inferred. |
| `VECTOR_SEARCH_INDEX_ID` | Required when `VECTOR_STORE=vertex`. |
| `JWT_SECRET` | Required in production, at least 32 characters. |
| `REQUIRE_AUTHENTICATION` | Off only for a local run against a mock. |
| `RATE_LIMIT_*_PER_MINUTE` | Per route family, per caller. |

`/health` reports every configuration problem it finds, and reports `degraded` when there is one.
It never returns a key, a token, or a prompt.

## Security

- **No credential in the client.** The desktop client holds a bearer token and nothing else.
- **Bearer tokens only.** Issued for development, and in production the client presents a token
  obtained from Google's own identity. The token endpoint does not exist there.
- **Rate limits per caller and per family.** Vision is limited separately from chat because it costs
  more, and exhausting one does not exhaust the other.
- **Images are not stored.** They are validated for type and size, held in memory for the length of
  one request, and never written to disk or to a log.
- **Logs carry metadata, not content.** Method, route, status, duration, caller, token count. Never
  a prompt, a document, an image or a token.
- **Tools are a registry.** The agent's plan is model output; only names the registry offers can run.
- **The container runs as a non-root user** with no write access outside its own directory.

## Google Cloud setup

Everything below needs `gcloud auth login` and a project you own.

```powershell
$project = "your-gcp-project-id"
$region  = "us-central1"

gcloud config set project $project

# The APIs this service calls. Without the vector API enabled, only knowledge retrieval fails.
gcloud services enable `
  run.googleapis.com `
  artifactregistry.googleapis.com `
  aiplatform.googleapis.com `
  cloudbuild.googleapis.com `
  secretmanager.googleapis.com

# Artifact Registry, where the image is pushed. One repository per region is the shape the
# gcloud tooling expects.
gcloud artifacts repositories create ai-assistant `
  --repository-format=docker --location=$region
```

### A vector index, if you want one persisted

```powershell
gcloud ai indexes create assistant-knowledge `
  --display-name=assistant-knowledge `
  --region=$region `
  --dimensions=768 `
  --distance=COSINE `
  --metadata-filterable=false `
  --description="Knowledge base for the AI Assistant"
```

Then set `VECTOR_STORE=vertex` and `VECTOR_SEARCH_INDEX_ID=<the id it printed>`. Until an index
exists, leave `VECTOR_STORE=in-memory`: the service starts, answers without a knowledge base, and
says so on `/health`, which is better than failing to start on a variable that was never set.

### The signing secret

```powershell
$secret = python -c "import secrets; print(secrets.token_urlsafe(48))"
gcloud secrets create ai-assistant-jwt --replication-policy=automatic
"${secret}:jwt" | gcloud secrets versions add ai-assistant-jwt --data-file=-
```

Store the value in Secret Manager rather than in the revision's environment, and mount it as
`JWT_SECRET`. A secret in a revision's environment is visible to anyone who can describe the
revision.

### Build and deploy

```powershell
gcloud builds submit `
  --tag "$region-docker.pkg.dev/$project/ai-assistant/backend:1.0.0"

gcloud run deploy ai-assistant-backend `
  --image "$region-docker.pkg.dev/$project/ai-assistant/backend:1.0.0" `
  --region $region `
  --platform managed `
  --service-account "ai-assistant@$project.iam.gserviceaccount.com" `
  --no-allow-unauthenticated `
  --set-env-vars "ENVIRONMENT=production,PROJECT_ID=$project,REGION=$region" `
  --set-env-vars "VECTOR_STORE=in-memory" `
  --set-secrets "JWT_SECRET=ai-assistant-jwt:latest" `
  --min-instances 0 `
  --max-instances 4 `
  --concurrency 80 `
  --timeout 120 `
  --memory 1Gi `
  --cpu 1
```

What each flag is doing, and why:

- `--no-allow-unauthenticated` because the Cloud Run IAM invoker role is the outer gate; the service
  checks its own bearer token as the inner one. Both are configured in deployment rather than in
  code, so neither can be forgotten in a commit.
- `--service-account` because that account's Application Default Credentials are how the service
  reaches Gemini and, later, the vector index. It needs `roles/aiplatform.user` and nothing else.
- `--min-instances 0` because a competition deployment should not cost anything while nobody is
  demonstrating it. Expect a cold start of a second or two.
- `--concurrency 80` because the service waits on I/O rather than computing, so one instance serves
  many requests. Note that the rate limiter's counters are per instance: with four instances the
  effective limit is four times the configured one.
- `--timeout 120` above the 60-second model timeout, so the client sees the service's own error
  rather than the platform's.

### Verify the deployment

```powershell
$url = (gcloud run services describe ai-assistant-backend --region $region --format="value(status.url)")

# Health needs no credential, on purpose: a load balancer has none.
curl.exe $url/health

# Everything else needs an identity token. Get one as the service account's operator:
$token = (gcloud auth print-identity-token)
curl.exe -H "Authorization: Bearer $token" -H "Content-Type: application/json" `
  -d '{"message":"Say hello in one sentence."}' "$url/api/chat"
```

A deployed response should carry `authenticationRequired: true` and a `requestId`, and its log line
should be visible in Cloud Logging with the caller and nothing else:

```powershell
gcloud logging read "resource.type=cloud_run_revision AND severity>=INFO" `
  --region $region --limit 20 --format="table(textPayload)"
```

## Deploying without Cloud Build

Useful when the container has to be built on a machine and pushed by hand.

```powershell
docker build -t ai-assistant-backend:1.0.0 .
docker tag ai-assistant-backend:1.0.0 "$region-docker.pkg.dev/$project/ai-assistant/backend:1.0.0"
docker push "$region-docker.pkg.dev/$project/ai-assistant/backend:1.0.0"
gcloud run deploy ai-assistant-backend --image "$region-docker.pkg.dev/$project/ai-assistant/backend:1.0.0" --region $region
```

Then `gcloud auth configure-docker "$region-docker.pkg.dev"` once per machine.

## Costs

At rest, a service with `--min-instances 0` costs nothing. Per request it costs what Gemini costs
plus a fraction of a second of Cloud Run. The rate limits are the control that matters: chat 30,
vision 10, embeddings 120, agent 10 per minute per caller.
