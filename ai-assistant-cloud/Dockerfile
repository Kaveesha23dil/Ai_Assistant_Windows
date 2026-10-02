# syntax=docker/dockerfile:1

# ---------------------------------------------------------------- base
# A pinned Python on a pinned Debian. Both are pinned deliberately: an unpinned base is how a
# rebuild silently changes the OpenSSL that a TLS call depends on.
FROM python:3.12-slim-bookworm

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

# ---------------------------------------------------------------- non-root
# The service runs as a user that owns nothing and can write nothing outside its own directory.
# A container process running as root is the difference between a container escape being a
# disaster and being an inconvenience.
RUN groupadd --system --gid 1001 appuser \
 && useradd --system --uid 1001 --gid appuser --no-create-home appuser

WORKDIR /app

# ---------------------------------------------------------------- dependencies
# Copied on their own so that a change to the source does not reinstall every package. Without
# this layer, every deploy is a full dependency resolution.
COPY requirements.txt ./

RUN python -m pip install --no-cache-dir --upgrade pip \
 && python -m pip install --no-cache-dir -r requirements.txt \
 && python -m pip uninstall --yes --no-dependencies pytest pytest-asyncio

# ---------------------------------------------------------------- source
COPY app ./app

# ---------------------------------------------------------------- hardening
# No .env, no credentials, no tests in the serving image. Anything copied in here is copied into
# something that will run, so the list is short and the .dockerignore below enforces it.
USER appuser

EXPOSE 8080

ENV PORT=8080

# The health check is the same endpoint a load balancer uses, so a revision that cannot serve
# traffic is taken out of rotation by the same signal that tells an operator.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD python -c "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8080/health', timeout=4).status == 200 else 1)"

# One worker and several threads rather than several workers. The vector store and the rate
# limiter are in-process, so several workers would mean several sets of counters and several
# copies of the in-memory index; the horizontal scaling is several instances, not several
# processes in one.
#
# Shell form rather than exec form, because $PORT has to be expanded by a shell: in exec form the
# argument reaches uvicorn as the literal string "${PORT}" and the container would fail to bind on
# the first deploy. Cloud Run sets PORT itself and overrides it, so the default here is only what a
# local `docker run` gets.
CMD uvicorn app.main:app \
    --host 0.0.0.0 \
    --port ${PORT:-8080} \
    --workers 1 \
    --timeout-keep-alive 65 \
    --no-server-header \
    --no-access-log \
    --log-level info
